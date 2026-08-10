using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.Client.Service;

public sealed partial class GameLifecycleWorker(
    ITransferSessionStore store,
    IGameSaveAdapter adapter,
    TransferOrchestrator orchestrator,
    TransferTransitionGate transitionGate,
    GameLifecycleMonitor monitor,
    TimeProvider timeProvider,
    ILogger<GameLifecycleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ObserveOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                LogObservationFailure(logger, exception);
            }

        }
    }

    private async Task ObserveOnceAsync(CancellationToken cancellationToken)
    {
        var active = await store.ReadActiveAsync(cancellationToken);
        var process = await adapter.DetectGameProcessAsync(cancellationToken);
        var decision = monitor.Observe(active, process.IsRunning);
        if (decision.LocalSessionId is not Guid sessionId) return;

        var result = await transitionGate.RunAsync(async () => decision.Action switch
        {
            GameLifecycleAction.ConfirmPlaceholder =>
                await orchestrator.ConfirmPlaceholderReadyAsync(sessionId, cancellationToken),
            GameLifecycleAction.MarkPlayStarted =>
                await orchestrator.MarkPlayStartedAsync(sessionId, cancellationToken),
            GameLifecycleAction.CompletePlay =>
                await orchestrator.CompletePlayAsync(sessionId, cancellationToken),
            _ => null
        }, cancellationToken);

        if (result is not null)
            LogTransition(logger, sessionId, decision.Action, result.Code, result.Success);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Surveillance du jeu, session {SessionId}: {Action} -> {Code} (succès={Success})")]
    private static partial void LogTransition(ILogger logger, Guid sessionId, GameLifecycleAction action, string code, bool success);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La surveillance automatique de Planet Crafter a échoué pour ce cycle.")]
    private static partial void LogObservationFailure(ILogger logger, Exception exception);
}
