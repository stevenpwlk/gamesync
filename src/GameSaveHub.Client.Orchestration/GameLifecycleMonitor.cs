namespace GameSaveHub.Client.Orchestration;

public enum GameLifecycleAction
{
    None,
    ConfirmPlaceholder,
    MarkPlayStarted,
    CompletePlay
}

public sealed record GameLifecycleDecision(GameLifecycleAction Action, Guid? LocalSessionId = null);

public sealed class GameLifecycleMonitor
{
    private Guid? _trackedSessionId;
    private bool _observedGameRunning;

    public GameLifecycleDecision Observe(IReadOnlyList<TransferSession> activeSessions, bool gameRunning)
    {
        ArgumentNullException.ThrowIfNull(activeSessions);
        if (activeSessions.Count != 1)
        {
            Reset();
            return new(GameLifecycleAction.None);
        }

        var session = activeSessions[0];
        if (_trackedSessionId != session.LocalSessionId)
        {
            _trackedSessionId = session.LocalSessionId;
            _observedGameRunning = false;
        }

        if (session.Stage is TransferStage.ManualReview or TransferStage.Interrupted)
            return new(GameLifecycleAction.None, session.LocalSessionId);

        if (gameRunning) _observedGameRunning = true;

        return session.Stage switch
        {
            TransferStage.ReadyToPlay when gameRunning =>
                new(GameLifecycleAction.MarkPlayStarted, session.LocalSessionId),
            TransferStage.AwaitingPlaceholder when !gameRunning && _observedGameRunning =>
                CompleteCycle(GameLifecycleAction.ConfirmPlaceholder, session.LocalSessionId),
            TransferStage.InGame when !gameRunning =>
                new(GameLifecycleAction.CompletePlay, session.LocalSessionId),
            _ => new(GameLifecycleAction.None, session.LocalSessionId)
        };
    }

    private GameLifecycleDecision CompleteCycle(GameLifecycleAction action, Guid sessionId)
    {
        _observedGameRunning = false;
        return new(action, sessionId);
    }

    private void Reset()
    {
        _trackedSessionId = null;
        _observedGameRunning = false;
    }
}
