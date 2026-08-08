namespace GameSaveHub.Contracts;

public sealed record ApiError(string Code, string Message, string CorrelationId);
public sealed record EnrollmentRedeemRequest(string Code, string DeviceName, string PublicKeyPem);
public sealed record EnrollmentRedeemResponse(Guid DeviceId);
public sealed record AuthChallengeRequest(Guid DeviceId);
public sealed record AuthChallengeResponse(Guid ChallengeId, string Nonce, DateTimeOffset ExpiresAtUtc);
public sealed record AuthTokenRequest(Guid DeviceId, Guid ChallengeId, string Signature);
public sealed record AuthTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
public sealed record WorldStatusResponse(Guid WorldId, string Name, string Status, Guid? CurrentVersionId, Guid? ActiveSessionId);
public sealed record AcquireWorldRequest(Guid? ExpectedVersionId);
public sealed record AcquireWorldResponse(Guid SessionId, Guid? CurrentVersionId, string State);
public sealed record SessionHeartbeatRequest(string ClientState);
public sealed record CreateUploadRequest(long Length, string Sha256, Guid? BaseVersionId, int ChunkSize);
public sealed record CreateUploadResponse(Guid UploadId, int ChunkSize, IReadOnlyList<int> ReceivedChunks);
public sealed record CommitUploadResponse(Guid VersionId, string Sha256, long Length);
public sealed record ReportFailureRequest(string Code, string Message);


public sealed record WorldCatalogItemResponse(
    Guid WorldId,
    string Name,
    string Status,
    Guid? CurrentVersionId,
    bool HasArtifact);

public sealed record WorldPreviewPlayerResponse(
    int Id,
    string Name,
    bool IsHost,
    int InventoryId,
    int EquipmentId);

public sealed record WorldPreviewResponse(
    Guid WorldId,
    string Name,
    string Status,
    Guid? CurrentVersionId,
    string? ArtifactSha256,
    string? AdapterId,
    string? SaveDisplayName,
    long? WorldSeed,
    IReadOnlyList<WorldPreviewPlayerResponse> Players,
    bool HasArtifact);
