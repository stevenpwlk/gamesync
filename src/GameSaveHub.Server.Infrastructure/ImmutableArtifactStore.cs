using System.Security.Cryptography;
using GameSaveHub.Core;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Server.Infrastructure;

public sealed class ImmutableArtifactStore(IOptions<StorageOptions> options)
{
    private readonly StorageOptions _options = options.Value;

    public string GetChunkPath(Guid uploadId, int index) =>
        Path.Combine(GetPendingDirectory(uploadId), $"{index:D8}.chunk");

    public string GetPendingDirectory(Guid uploadId) =>
        Path.Combine(GetRoot(), "pending", uploadId.ToString("N"));

    public string GetObjectPath(string sha256) => Path.Combine(
        GetRoot(), "objects", sha256[..2], sha256[2..4], sha256 + ".gshsave");

    public async Task<(long Length, string Sha256)> PutChunkAsync(
        Guid uploadId,
        int index,
        Stream source,
        int expectedMaximum,
        CancellationToken cancellationToken)
    {
        if (index < 0)
        {
            throw new InvalidOperationException("Index de chunk invalide.");
        }

        Directory.CreateDirectory(GetPendingDirectory(uploadId));
        var destination = GetChunkPath(uploadId, index);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            long length = 0;
            string sha256;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
                {
                    var buffer = new byte[131072];
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer, cancellationToken);
                        if (read == 0) break;
                        length += read;
                        if (length > expectedMaximum || length > _options.MaxChunkBytes)
                        {
                            throw new InvalidOperationException("Chunk surdimensionné.");
                        }

                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }

                    await output.FlushAsync(cancellationToken);
                    output.Flush(true);
                }

                sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            if (File.Exists(destination))
            {
                var existingHash = await FileSafety.ComputeSha256Async(destination, cancellationToken);
                var existingLength = new FileInfo(destination).Length;
                if (existingHash != sha256 || existingLength != length)
                {
                    throw new InvalidOperationException("Le chunk existe déjà avec un contenu différent.");
                }

                File.Delete(temporary);
            }
            else
            {
                File.Move(temporary, destination);
            }

            return (length, sha256);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<string> AssembleAndPublishAsync(UploadEntity upload, CancellationToken cancellationToken)
    {
        if (upload.Length <= 0 || upload.Length > _options.MaxArtifactBytes)
        {
            throw new InvalidOperationException("Taille totale d'artefact invalide.");
        }

        var expectedChunks = checked((int)((upload.Length + upload.ChunkSize - 1) / upload.ChunkSize));
        var assembling = Path.Combine(GetPendingDirectory(upload.Id), "assembled.tmp");
        await using (var output = new FileStream(assembling, FileMode.Create, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
        {
            for (var index = 0; index < expectedChunks; index++)
            {
                var chunk = GetChunkPath(upload.Id, index);
                if (!File.Exists(chunk)) throw new InvalidOperationException($"Chunk manquant : {index}.");
                await using var input = new FileStream(chunk, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous);
                await input.CopyToAsync(output, cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
            output.Flush(true);
        }

        if (new FileInfo(assembling).Length != upload.Length)
        {
            throw new InvalidOperationException("La taille assemblée ne correspond pas au manifeste.");
        }

        var hash = await FileSafety.ComputeSha256Async(assembling, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(upload.Sha256)))
        {
            throw new InvalidOperationException("Le hash assemblé ne correspond pas au manifeste.");
        }

        await ArtifactEnvelopeValidator.ValidateAsync(assembling, _options.MaxArtifactBytes, cancellationToken);

        var destination = GetObjectPath(hash);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            File.Delete(assembling);
        }
        else
        {
            File.Move(assembling, destination);
        }

        return destination;
    }

    public async Task<ImportedArtifactObject> ImportLocalAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        await using var staged = await StageLocalAsync(sourcePath, cancellationToken);
        return await PublishStagedAsync(staged, cancellationToken);
    }

    public async Task<StagedLocalArtifact> StageLocalAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var sourceInfo = new FileInfo(source);
        if (!sourceInfo.Exists || sourceInfo.Length <= 0 || sourceInfo.Length > _options.MaxArtifactBytes)
            throw new InvalidOperationException("Taille d'artefact local invalide.");

        var stagingDirectory = Path.Combine(GetRoot(), "pending", "local-imports");
        Directory.CreateDirectory(stagingDirectory);
        var stagedPath = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".gshsave");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(true);
            }

            var stagedInfo = new FileInfo(stagedPath);
            if (stagedInfo.Length != sourceInfo.Length || stagedInfo.Length <= 0 || stagedInfo.Length > _options.MaxArtifactBytes)
                throw new InvalidOperationException("Le monde a changé pendant la copie locale.");

            var summary = await ArtifactEnvelopeValidator.ReadSummaryAsync(
                stagedPath,
                _options.MaxArtifactBytes,
                cancellationToken);
            var hash = await FileSafety.ComputeSha256Async(stagedPath, cancellationToken);
            return new StagedLocalArtifact(stagedPath, hash, stagedInfo.Length, summary);
        }
        catch
        {
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
            throw;
        }
    }

    public async Task<ImportedArtifactObject> PublishStagedAsync(
        StagedLocalArtifact staged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staged);
        var stagedInfo = new FileInfo(staged.Path);
        if (!stagedInfo.Exists || stagedInfo.Length != staged.Length)
            throw new InvalidOperationException("Le snapshot validé est absent ou a changé.");
        var stagedHash = await FileSafety.ComputeSha256Async(staged.Path, cancellationToken);
        if (!stagedHash.Equals(staged.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le snapshot validé a changé avant sa publication.");

        var destination = GetObjectPath(staged.Sha256);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            var existingInfo = new FileInfo(destination);
            var existingHash = await FileSafety.ComputeSha256Async(destination, cancellationToken);
            if (existingInfo.Length != staged.Length ||
                !existingHash.Equals(staged.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Collision ou objet immuable existant corrompu.");

            return new ImportedArtifactObject(destination, staged.Sha256, existingInfo.Length, staged.Summary);
        }

        try
        {
            File.Move(staged.Path, destination);
        }
        catch (IOException) when (File.Exists(destination))
        {
            var concurrentInfo = new FileInfo(destination);
            var concurrentHash = await FileSafety.ComputeSha256Async(destination, cancellationToken);
            if (concurrentInfo.Length != staged.Length ||
                !concurrentHash.Equals(staged.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Collision ou objet immuable concurrent corrompu.");
        }

        return new ImportedArtifactObject(destination, staged.Sha256, staged.Length, staged.Summary);
    }

    /// <summary>
    /// Supprime les chunks d'un upload une fois la publication définitivement acquise.
    /// </summary>
    /// <remarks>
    /// À n'appeler qu'<b>après</b> la validation de la transaction SQLite. Tant qu'elle
    /// n'est pas confirmée, un commit rejoué doit pouvoir réassembler l'artefact depuis
    /// ces chunks : les effacer plus tôt rendrait la reprise impossible.
    /// L'échec du nettoyage n'est jamais remonté — la version publiée est valide, et
    /// laisser un résidu vaut mieux que faire échouer une publication réussie.
    /// </remarks>
    public void TryCleanupPending(Guid uploadId)
    {
        try
        {
            var directory = GetPendingDirectory(uploadId);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Résidu conservé : sans conséquence sur la version publiée.
        }
    }

    private string GetRoot() => Path.GetFullPath(_options.Root);
}

public sealed record ImportedArtifactObject(
    string Path,
    string Sha256,
    long Length,
    ArtifactEnvelopeSummary Summary);

public sealed class StagedLocalArtifact(
    string path,
    string sha256,
    long length,
    ArtifactEnvelopeSummary summary) : IAsyncDisposable
{
    public string Path { get; } = path;
    public string Sha256 { get; } = sha256;
    public long Length { get; } = length;
    public ArtifactEnvelopeSummary Summary { get; } = summary;

    public ValueTask DisposeAsync()
    {
        if (File.Exists(Path)) File.Delete(Path);
        return ValueTask.CompletedTask;
    }
}
