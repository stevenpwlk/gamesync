using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Orchestration;

public interface ITransferServerClient
{
    Task<IReadOnlyList<WorldCatalogItemResponse>> ListWorldsAsync(CancellationToken cancellationToken = default);
    Task<WorldPreviewResponse> GetWorldPreviewAsync(Guid worldId, CancellationToken cancellationToken = default);
    Task<WorldStatusResponse> GetWorldStatusAsync(Guid worldId, CancellationToken cancellationToken = default);
    Task<AcquireWorldResponse> AcquireWorldAsync(Guid worldId, Guid? expectedVersionId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<ServerArtifactDownload> DownloadSessionArtifactAsync(Guid serverSessionId, string destinationPath, CancellationToken cancellationToken = default);
    Task MarkImportStartingAsync(Guid serverSessionId, CancellationToken cancellationToken = default);
    Task HeartbeatAsync(Guid serverSessionId, string clientState, CancellationToken cancellationToken = default);
    Task<CreateUploadResponse> CreateUploadAsync(Guid serverSessionId, CreateUploadRequest request, CancellationToken cancellationToken = default);
    Task PutUploadChunkAsync(Guid uploadId, int index, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);
    Task<CommitUploadResponse> CommitUploadAsync(Guid uploadId, CancellationToken cancellationToken = default);
    Task AbortSessionAsync(Guid serverSessionId, CancellationToken cancellationToken = default);
    Task ReportFailureAsync(Guid serverSessionId, string code, string message, CancellationToken cancellationToken = default);
}
