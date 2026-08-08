namespace GameSaveHub.Client.Orchestration;

public interface ITransferSessionStore
{
    string RootPath { get; }
    string GetSessionDirectory(Guid localSessionId);
    Task<TransferSession?> ReadAsync(Guid localSessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransferSession>> ReadActiveAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(TransferSession session, string eventName, CancellationToken cancellationToken = default);
}
