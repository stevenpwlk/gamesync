using GameSaveHub.Core;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Server.Infrastructure;

/// <summary>
/// Stockage adressé par contenu pour les paquets de release client (.zip). Ne valide
/// aucune enveloppe .gshsave — contrairement à <see cref="ImmutableArtifactStore"/>,
/// dont l'objet stocké est toujours une sauvegarde de jeu, ici c'est un installateur.
/// </summary>
public sealed class ClientReleaseObjectStore(IOptions<StorageOptions> options)
{
    private readonly StorageOptions _options = options.Value;

    public string GetObjectPath(string sha256, string version) => Path.Combine(
        GetRoot(), "objects", "client-releases", sha256[..2], sha256[2..4], $"{version}.zip");

    public async Task<string> PutAsync(string sourcePath, string sha256, string version, CancellationToken cancellationToken = default)
    {
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists || sourceInfo.Length <= 0 || sourceInfo.Length > _options.MaxArtifactBytes)
            throw new InvalidOperationException("Taille de paquet de release invalide.");

        var actualHash = await FileSafety.ComputeSha256Async(sourcePath, cancellationToken);
        if (!actualHash.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le hash du paquet ne correspond pas au manifeste.");

        var destination = GetObjectPath(sha256, version);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            var existingHash = await FileSafety.ComputeSha256Async(destination, cancellationToken);
            if (!existingHash.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Collision ou objet de release existant corrompu.");
            return destination;
        }

        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(sourcePath, temporary, overwrite: false);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return destination;
    }

    private string GetRoot() => Path.GetFullPath(_options.Root);
}
