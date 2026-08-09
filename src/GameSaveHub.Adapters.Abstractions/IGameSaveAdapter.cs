using GameSaveHub.Contracts;

namespace GameSaveHub.Adapters.Abstractions;

public interface IGameSaveAdapter
{
    string Id { get; }
    AdapterCapabilityReport Capabilities { get; }

    Task<InstallationDetection> DetectInstallationAsync(CancellationToken cancellationToken = default);
    Task<LocalStorageInspection> InspectLocalStorageAsync(CancellationToken cancellationToken = default);
    Task<SnapshotResult> CreateSafetySnapshotAsync(string outputRoot, string? acknowledgedTestWorldName, CancellationToken cancellationToken = default);
    Task<PortableSaveArtifact> ExportPortableArtifactAsync(string worldName, string outputRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exporte en désignant le monde par son nom logique, seul identifiant unique.
    /// Le nom affiché ne l'est pas : deux imports de la même sauvegarde produisent
    /// deux mondes homonymes.
    /// </summary>
    Task<PortableSaveArtifact> ExportPortableArtifactByLogicalNameAsync(string logicalName, string outputRoot, CancellationToken cancellationToken = default);
    Task<ArtifactValidation> ValidateArtifactAsync(PortableSaveArtifact artifact, CancellationToken cancellationToken = default);
    Task<HostPreparation> PrepareForHostAsync(PortableSaveArtifact artifact, string playerName, string targetDisplayName, string outputRoot, CancellationToken cancellationToken = default);
    Task<ImportBaselineResult> CreateImportBaselineAsync(string outputRoot, CancellationToken cancellationToken = default);
    Task<ImportTargetProbeResult> ProbeImportTargetAsync(
        string baselineDirectory,
        string expectedPlaceholderName,
        CancellationToken cancellationToken = default);
    Task<ImportReconciliationResult> ReconcilePortableImportAsync(
        PortableSaveArtifact artifact,
        string baselineDirectory,
        string expectedPlayerName,
        string targetLogicalName,
        string placeholderPayloadSha256,
        CancellationToken cancellationToken = default);
    Task<PortableImportResult> ImportPortableArtifactAsync(
        PortableSaveArtifact artifact,
        string baselineDirectory,
        string expectedPlayerName,
        string expectedPlaceholderName,
        string preImportBackupOutputRoot,
        CancellationToken cancellationToken = default);
    Task<GameLaunch> LaunchGameAsync(CancellationToken cancellationToken = default);
    Task<GameProcessDetection> DetectGameProcessAsync(CancellationToken cancellationToken = default);
    Task<SaveStability> WaitForSaveStabilityAsync(TimeSpan observationWindow, CancellationToken cancellationToken = default);
}
