using GameSaveHub.Core;

namespace GameSaveHub.Server.Infrastructure;

public sealed class WorldEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class DeviceEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string PublicKeyPem { get; set; }
    public DateTimeOffset EnrolledAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class EnrollmentEntity
{
    public Guid Id { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RedeemedAtUtc { get; set; }
    public Guid? DeviceId { get; set; }
}

public sealed class AuthChallengeEntity
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public required byte[] Nonce { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
}

public sealed class SessionEntity
{
    public Guid Id { get; set; }
    public Guid WorldId { get; set; }
    public Guid DeviceId { get; set; }
    public string? PlayerName { get; set; }
    public Guid? BaseVersionId { get; set; }
    public WorldSessionState State { get; set; }
    public bool ImportStarted { get; set; }
    public required string LastClientState { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public sealed class SaveVersionEntity
{
    public Guid Id { get; set; }
    public Guid WorldId { get; set; }
    public Guid? SessionId { get; set; }
    public required string Sha256 { get; set; }
    public long Length { get; set; }
    public required string StorageKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsProtected { get; set; }
    public string? ProtectionReason { get; set; }
    public string? PublishedByPlayerName { get; set; }
}

public sealed class AdminAuditEntity
{
    public Guid Id { get; set; }
    public required string Action { get; set; }
    public required string TargetId { get; set; }
    public required string Reason { get; set; }
    public string? DetailsJson { get; set; }
    public DateTimeOffset PerformedAtUtc { get; set; }
}

public sealed class UploadEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? BaseVersionId { get; set; }
    public required string Sha256 { get; set; }
    public long Length { get; set; }
    public int ChunkSize { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CommittedAtUtc { get; set; }
    public Guid? ResultVersionId { get; set; }
}

public sealed class UploadChunkEntity
{
    public Guid UploadId { get; set; }
    public int Index { get; set; }
    public long Length { get; set; }
    public required string Sha256 { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class IdempotencyEntity
{
    public Guid DeviceId { get; set; }
    public required string Key { get; set; }
    public required string Route { get; set; }
    public int StatusCode { get; set; }
    public required string ResponseJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
