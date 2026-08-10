namespace GameSaveHub.Client.Service;

public sealed class ClientServiceOptions
{
    public const string SectionName = "ClientService";
    public string PipeName { get; set; } = "GameSaveHub.Client";
    public string RegisteredUserSid { get; set; } = string.Empty;
    public string RegisteredUserLocalAppData { get; set; } = string.Empty;
    public string ServerBaseUrl { get; set; } = "https://saves.stevenpwlk.fr:18443/";
    public string StatePath { get; set; } = "%ProgramData%\\GameSaveHub\\client-state.json";
    public string TransferRootPath { get; set; } = "%ProgramData%\\GameSaveHub\\transfers";
    public string ManagedSlotStatePath { get; set; } = "%ProgramData%\\GameSaveHub\\managed-slot.json";
    public string CngKeyName { get; set; } = "GameSaveHub.DeviceIdentity";
    public bool EnableWgsTransfer { get; set; }
}

public sealed record ClientPersistentState(
    int SchemaVersion,
    Guid? DeviceId,
    string? DeviceName,
    DateTimeOffset? EnrolledAtUtc,
    Guid? ActiveSessionId,
    Guid? ActiveWorldId,
    string? ActiveSessionState,
    IReadOnlyList<PendingUploadState> PendingUploads,
    string? RegisteredPlayerName = null);

public sealed record PendingUploadState(
    Guid SessionId,
    Guid UploadId,
    string ArtifactPath,
    string Sha256,
    long Length,
    IReadOnlyList<int> ConfirmedChunks);

public sealed record PipeRequest(
    string Command,
    string? EnrollmentCode = null,
    string? DeviceName = null,
    Guid? WorldId = null,
    string? PlayerName = null,
    Guid? TransferSessionId = null);
public sealed record PipeResponse(bool Success, string Code, string Message, object? Data = null);
