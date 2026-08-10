namespace GameSaveHub.Client.Orchestration;

public enum TransferFlowKind
{
    LegacyPlaceholder,
    InitialSlotSetup,
    ManagedSlotReuse
}

public enum TransferStage
{
    Initialized,
    Acquiring,
    DownloadingArtifact,
    PreparingArtifact,
    CreatingBaseline,
    AwaitingPlaceholder,
    Importing,
    ReadyToPlay,
    InGame,
    CapturingResult,
    UploadPending,
    Uploading,
    Publishing,
    Completed,
    Interrupted,
    Aborted,
    Failed,
    ManualReview
}

public static class TransferStageRules
{
    public static bool IsTerminal(TransferStage stage) => stage is TransferStage.Completed
        or TransferStage.Aborted
        or TransferStage.Failed;

    public static bool HoldsLocalLock(TransferStage stage) => !IsTerminal(stage);

    public static bool CanAbortBeforeImport(TransferSession session) =>
        !session.ServerImportStarted && session.Stage is not TransferStage.Completed and not TransferStage.Aborted;
}

public sealed record TransferSession(
    int SchemaVersion,
    Guid LocalSessionId,
    Guid WorldId,
    Guid? ServerSessionId,
    Guid? BaseVersionId,
    string PlayerName,
    string AcquireIdempotencyKey,
    string? PlaceholderName,
    TransferStage Stage,
    TransferStage? ResumeStage,
    int Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? DownloadedArtifactPath,
    string? DownloadedArtifactSha256,
    string? PreparedArtifactPath,
    string? PreparedArtifactSha256,
    string? PreparedPayloadSha256,
    string? BaselineDirectory,
    string? TargetLogicalName,
    string? TargetDisplayName,
    string? PlaceholderPayloadSha256,
    string? PreImportSnapshotDirectory,
    string? ImportedPayloadSha256,
    bool ServerImportStarted,
    string? OutboundArtifactPath,
    string? OutboundArtifactSha256,
    long? OutboundArtifactLength,
    Guid? UploadId,
    int? UploadChunkSize,
    IReadOnlyList<int> ConfirmedChunks,
    Guid? ResultVersionId,
    string? LastErrorCode,
    string? LastErrorMessage,
    TransferFlowKind FlowKind = TransferFlowKind.LegacyPlaceholder,
    string? ManagedSlotCurrentDisplayName = null,
    string? ManagedSlotDesiredDisplayName = null,
    bool ManagedSlotBindingCommitted = false)
{
    public const int CurrentSchemaVersion = 1;

    public static TransferSession Create(Guid worldId, string playerName, DateTimeOffset now, TransferFlowKind flowKind = TransferFlowKind.LegacyPlaceholder) => new(
        CurrentSchemaVersion,
        Guid.NewGuid(),
        worldId,
        null,
        null,
        playerName,
        Guid.NewGuid().ToString("N"),
        null,
        TransferStage.Initialized,
        null,
        0,
        now,
        now,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        null,
        null,
        null,
        null,
        null,
        [],
        null,
        null,
        null,
        flowKind);
}

public sealed record TransferOperationResult(
    bool Success,
    string Code,
    string Message,
    TransferSession? Session);

public sealed record ServerArtifactDownload(
    bool HasArtifact,
    string? Path,
    string? Sha256,
    long? Length);

public sealed class TransferServerException : InvalidOperationException
{
    public TransferServerException() : this("server_error", "Erreur serveur GameSave Hub.") { }
    public TransferServerException(string message) : this("server_error", message) { }
    public TransferServerException(string message, Exception innerException) : base(message, innerException) => Code = "server_error";
    public TransferServerException(string code, string message) : base(message) => Code = code;
    public TransferServerException(string code, string message, Exception innerException) : base(message, innerException) => Code = code;

    public string Code { get; }
}
