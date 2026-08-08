using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.Client.Service;

public sealed partial class TransferRecoveryWorker(
    TransferOrchestrator orchestrator,
    ILogger<TransferRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var results = await orchestrator.RecoverAllAsync(stoppingToken);
            foreach (var result in results)
            {
                LogRecovery(logger, result.Session?.LocalSessionId, result.Code, result.Message);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Arrêt normal du service.
        }
        catch (Exception exception)
        {
            LogRecoveryFailure(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reprise session {SessionId}: {Code} - {Message}")]
    private static partial void LogRecovery(ILogger logger, Guid? sessionId, string code, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de la reprise des sessions locales.")]
    private static partial void LogRecoveryFailure(ILogger logger, Exception exception);
}
