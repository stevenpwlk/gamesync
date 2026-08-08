namespace GameSaveHub.Core;

public enum WorldSessionState
{
    Preparing,
    InGame,
    UploadPending,
    Publishing,
    Interrupted,
    Completed,
    Aborted,
    Failed
}

public static class WorldSessionStateMachine
{
    public static bool CanTransition(WorldSessionState from, WorldSessionState to) => (from, to) switch
    {
        (WorldSessionState.Preparing, WorldSessionState.InGame) => true,
        (WorldSessionState.Preparing, WorldSessionState.Aborted) => true,
        (WorldSessionState.Preparing, WorldSessionState.Interrupted) => true,
        (WorldSessionState.InGame, WorldSessionState.UploadPending) => true,
        (WorldSessionState.InGame, WorldSessionState.Interrupted) => true,
        (WorldSessionState.UploadPending, WorldSessionState.Publishing) => true,
        (WorldSessionState.UploadPending, WorldSessionState.Interrupted) => true,
        (WorldSessionState.Publishing, WorldSessionState.Completed) => true,
        (WorldSessionState.Publishing, WorldSessionState.Interrupted) => true,
        (WorldSessionState.Interrupted, WorldSessionState.UploadPending) => true,
        (WorldSessionState.Interrupted, WorldSessionState.Publishing) => true,
        (WorldSessionState.Interrupted, WorldSessionState.Failed) => true,
        (_, WorldSessionState.Failed) => from is not WorldSessionState.Completed and not WorldSessionState.Aborted,
        _ => false
    };

    public static void EnsureTransition(WorldSessionState from, WorldSessionState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Transition de session interdite : {from} -> {to}.");
        }
    }

    public static bool HoldsWorldLock(WorldSessionState state) => state is not WorldSessionState.Completed
        and not WorldSessionState.Aborted
        and not WorldSessionState.Failed;

    public static bool CanUserAbort(WorldSessionState state, bool importStarting) =>
        state == WorldSessionState.Preparing && !importStarting;
}
