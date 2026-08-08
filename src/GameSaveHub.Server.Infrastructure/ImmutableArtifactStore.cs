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
