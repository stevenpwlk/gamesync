using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Contracts;
using GameSaveHub.Core;

namespace GameSaveHub.SaveExporter.Core;

public sealed class SaveExporterService(IGameSaveAdapter adapter)
{
    public async Task<IReadOnlyList<SaveExportWorld>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var inspection = await adapter.InspectLocalStorageAsync(cancellationToken);
        return inspection.Worlds
            .Select(world =>
            {
                var blob = inspection.Files.SingleOrDefault(file =>
                    file.RelativePath.Equals(world.BlobRelativePath, StringComparison.OrdinalIgnoreCase));
                return new SaveExportWorld(
                    world.LogicalName,
                    world.DisplayName,
                    blob?.LastWriteUtc,
                    world.Mode,
                    world.Players
                        .Select(player => new SaveExportPlayer(player.Name, player.IsHost))
                        .ToArray());
            })
            .ToArray();
    }

    public async Task<PortableSaveArtifact> ExportAsync(
        string logicalName,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
            throw new ArgumentException("Le nom logique est requis.", nameof(logicalName));
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("Le dossier de destination est requis.", nameof(destinationDirectory));

        if (FileSafety.IsNetworkPath(destinationDirectory))
            throw new InvalidOperationException("Choisissez un dossier sur un disque local.");

        var destination = Path.GetFullPath(destinationDirectory);
        var existingFiles = Directory.Exists(destination)
            ? Directory.EnumerateFiles(destination)
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var artifact = await adapter.ExportPortableArtifactByLogicalNameAsync(
            logicalName.Trim(),
            destination,
            cancellationToken);
        var validation = await adapter.ValidateArtifactAsync(artifact, cancellationToken);
        if (validation.IsValid) return artifact;

        var artifactPath = Path.GetFullPath(artifact.Path);
        if (IsDirectChildOf(artifactPath, destination) && !existingFiles.Contains(artifactPath) && File.Exists(artifactPath))
            File.Delete(artifactPath);

        throw new InvalidDataException(
            validation.Errors.Count == 0
                ? "La validation finale de l'export a échoué."
                : string.Join(Environment.NewLine, validation.Errors));
    }

    private static bool IsDirectChildOf(string path, string directory) =>
        string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase);

}
