using GameSaveHub.Core;

namespace GameSaveHub.UnitTests;

public sealed class WorldSessionStateMachineTests
{
    [Theory]
    [InlineData(WorldSessionState.Preparing, WorldSessionState.InGame)]
    [InlineData(WorldSessionState.InGame, WorldSessionState.UploadPending)]
    [InlineData(WorldSessionState.UploadPending, WorldSessionState.Publishing)]
    [InlineData(WorldSessionState.Publishing, WorldSessionState.Completed)]
    [InlineData(WorldSessionState.Interrupted, WorldSessionState.UploadPending)]
    public void ValidTransitionsAreAccepted(WorldSessionState from, WorldSessionState to)
    {
        Assert.True(WorldSessionStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(WorldSessionState.InGame, WorldSessionState.Aborted)]
    [InlineData(WorldSessionState.Completed, WorldSessionState.Preparing)]
    [InlineData(WorldSessionState.Failed, WorldSessionState.InGame)]
    public void InvalidTransitionsAreRejected(WorldSessionState from, WorldSessionState to)
    {
        Assert.False(WorldSessionStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void InterruptedSessionKeepsLock()
    {
        Assert.True(WorldSessionStateMachine.HoldsWorldLock(WorldSessionState.Interrupted));
    }

    [Theory]
    [InlineData(WorldSessionState.Preparing, false, true)]
    [InlineData(WorldSessionState.Preparing, true, false)]
    [InlineData(WorldSessionState.InGame, false, false)]
    public void AbortRequiresPreparingBeforeImport(WorldSessionState state, bool importStarted, bool expected)
    {
        Assert.Equal(expected, WorldSessionStateMachine.CanUserAbort(state, importStarted));
    }
}
