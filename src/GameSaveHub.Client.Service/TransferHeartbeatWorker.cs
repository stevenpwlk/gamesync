using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.Client.Service;

public sealed partial class TransferHeartbeatWorker(
    ITransferSessionStore store,
    ITransferServerClient server,
    TimeProvider timeProvider,
    ILogger<TransferHeartbeatWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var sessions = await store.ReadActiveAsync(stoppingToken);
                foreach (var session in sessions)
                {
                    if (session.ServerSessionId is not Guid serverSessionId) continue;
                    try
                    {
                        await server.HeartbeatAsync(serverSessionId, session.Stage.ToString(), stoppingToken);
                    }
                    catch (TransferServerException exception)
                    {
                        LogHeartbeatFailure(logger, session.LocalSessionId, exception.Code, exception.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogWorkerFailure(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat session {SessionId} en échec : {Code} - {Message}")]
    private static partial void LogHeartbeatFailure(ILogger logger, Guid sessionId, string code, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec du worker de heartbeat des transferts.")]
    private static partial void LogWorkerFailure(ILogger logger, Exception exception);
}
