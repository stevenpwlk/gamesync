namespace GameSaveHub.Contracts;

public enum DiagnosticFileRole
{
    OpaqueBlob,
    ContainerMetadata,
    ContainerIndex,
    Unknown
}

public sealed record DiagnosticFile(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteUtc,
    string Sha256,
    DiagnosticFileRole Role,
    bool StableDuringRead);

public sealed record InstallationDetection(
    bool IsInstalled,
    string PackageFamilyName,
    string? PackageFullName,
    string? InstalledVersion,
    string? InstallLocation,
    string? LocalPackageRoot,
    string? WgsRoot,
    IReadOnlyList<string> Warnings);

public sealed record LocalStorageInspection(
    int SchemaVersion,
    string AdapterId,
    string PackageFamilyName,
    DateTimeOffset CapturedAtUtc,
    bool GameRunning,
    bool Stable,
    IReadOnlyList<string> RunningProcesses,
    IReadOnlyList<DiagnosticFile> Files,
    IReadOnlyList<DiscoveredWorld> Worlds,
    IReadOnlyList<string> Warnings);

public sealed record DiscoveredWorld(
    string LogicalName,
    string DisplayName,
    string? PlanetId,
    string? Mode,
    long? WorldSeed,
    string BlobRelativePath,
    IReadOnlyList<DiscoveredPlayer> Players);

public sealed record DiscoveredPlayer(
    int Id,
    string Name,
    bool IsHost,
    string? PlanetId,
    string? Position,
    int InventoryId,
    int EquipmentId);

public sealed record SafetySnapshotManifest(
    int SchemaVersion,
    string SnapshotId,
    string AdapterId,
    string PackageFamilyName,
    DateTimeOffset CapturedAtUtc,
    string AcknowledgedTestWorldName,
    IReadOnlyList<DiagnosticFile> Files);

public sealed record SnapshotResult(
    bool Success,
    string? SnapshotDirectory,
    SafetySnapshotManifest? Manifest,
    IReadOnlyList<string> Errors);

public sealed record SnapshotDifference(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Unchanged);

public sealed record LogicalFileDifference(
    string LogicalName,
    string Status,
    string? BeforeSha256,
    string? AfterSha256,
    long? BeforeLength,
    long? AfterLength);

public sealed record LogicalSnapshotDifference(IReadOnlyList<LogicalFileDifference> Files);

public sealed record AdapterCapabilityReport(
    bool CanInspect,
    bool CanCreateSafetySnapshot,
    bool CanExportPortableArtifact,
    bool CanPrepareForHost,
    bool CanImportPortableArtifact,
    bool CanLaunchGame,
    string GateStatus);

public sealed record PortableSaveArtifact(string Path, string Sha256, long Length, PortableArtifactManifest? Manifest);
public sealed record PortableArtifactManifest(
    int SchemaVersion,
    string AdapterId,
    DateTimeOffset CapturedAtUtc,
    string LogicalName,
    string DisplayName,
    string? PlanetId,
    string? Mode,
    long? WorldSeed,
    string PayloadPath,
    long PayloadLength,
    string PayloadSha256,
    IReadOnlyList<DiscoveredPlayer> Players);
public sealed record ArtifactValidation(bool IsValid, IReadOnlyList<string> Errors);
public enum HostPreparationOutcome
{
    Prepared,
    AlreadyHost,
    PlayerNotFound,
    PlayerAmbiguous,
    InvalidArtifact,
    InvalidPlayerTopology,
    Failed
}

public sealed record HostPreparation(
    bool Success,
    HostPreparationOutcome Outcome,
    PortableSaveArtifact? PreparedArtifact,
    string? TargetPlayerName,
    int? TargetPlayerOriginalId,
    int? PreviousHostPlayerId,
    bool Changed,
    IReadOnlyList<string> Errors);

public sealed record ImportProtectedWorld(
    string LogicalName,
    string DisplayName,
    long? WorldSeed,
    string PayloadSha256);

public sealed record ImportBaselineManifest(
    int SchemaVersion,
    string SnapshotId,
    string AdapterId,
    string PackageFamilyName,
    DateTimeOffset CapturedAtUtc,
    int MaximumStandardIndex,
    IReadOnlyList<ImportProtectedWorld> ProtectedWorlds,
    IReadOnlyList<DiagnosticFile> Files);

public sealed record ImportBaselineResult(
    bool Success,
    string? BaselineDirectory,
    ImportBaselineManifest? Manifest,
    IReadOnlyList<string> Errors);

public sealed record ImportTargetProbeResult(
    bool Success,
    string? TargetLogicalName,
    string? TargetDisplayName,
    string? PlaceholderPayloadSha256,
    IReadOnlyList<string> Errors);

public enum ImportReconciliationState
{
    PlaceholderIntact,
    ImportedArtifactPresent,
    TargetMissing,
    ProtectedWorldChanged,
    UnexpectedTargetContent,
    InvalidBaseline,
    InvalidArtifact
}

public sealed record ImportReconciliationResult(
    ImportReconciliationState State,
    string? TargetLogicalName,
    string? CurrentPayloadSha256,
    string? ExpectedImportedPayloadSha256,
    IReadOnlyList<string> Errors);

public sealed record PortableImportResult(
    bool Success,
    string? TargetLogicalName,
    string? TargetDisplayName,
    string? PreImportSnapshotDirectory,
    string? PreviousPayloadSha256,
    string? ImportedPayloadSha256,
    IReadOnlyList<string> Errors);
public sealed record GameLaunch(bool Success, int? ProcessId, IReadOnlyList<string> Errors);
public sealed record GameProcessDetection(bool IsRunning, IReadOnlyList<int> ProcessIds);
public sealed record SaveStability(bool IsStable, IReadOnlyList<string> ChangedFiles);

public sealed record TestWorldRestoreResult(
    bool Success,
    string? PreRestoreSnapshotDirectory,
    string? LogicalName,
    string? PreviousSha256,
    string? RestoredSha256,
    IReadOnlyList<string> Errors);

public sealed record DiagnosticSafetyStatus(
    bool GameRunning,
    bool ActiveNetworkRoute,
    bool SafeForOfflineTest);
