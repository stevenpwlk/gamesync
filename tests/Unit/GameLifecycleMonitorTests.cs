using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class GameLifecycleMonitorTests
{
    [Fact]
    public void ReadyToPlayAndRunningMarksPlayStarted()
    {
        var monitor = new GameLifecycleMonitor();

        var decision = monitor.Observe([Session(TransferStage.ReadyToPlay)], gameRunning: true);

        Assert.Equal(GameLifecycleAction.MarkPlayStarted, decision.Action);
    }

    [Fact]
    public void InGameAndClosedCompletesEvenAfterServiceRestart()
    {
        var monitor = new GameLifecycleMonitor();

        var decision = monitor.Observe([Session(TransferStage.InGame)], gameRunning: false);

        Assert.Equal(GameLifecycleAction.CompletePlay, decision.Action);
    }

    [Fact]
    public void PlaceholderRequiresAnObservedStartAndCloseCycle()
    {
        var monitor = new GameLifecycleMonitor();
        var session = Session(TransferStage.AwaitingPlaceholder);

        Assert.Equal(GameLifecycleAction.None, monitor.Observe([session], gameRunning: false).Action);
        Assert.Equal(GameLifecycleAction.None, monitor.Observe([session], gameRunning: true).Action);
        Assert.Equal(GameLifecycleAction.ConfirmPlaceholder, monitor.Observe([session], gameRunning: false).Action);
    }

    [Theory]
    [InlineData(TransferStage.ManualReview)]
    [InlineData(TransferStage.Interrupted)]
    public void UnsafeStagesNeverTransitionAutomatically(TransferStage stage)
    {
        var monitor = new GameLifecycleMonitor();

        var decision = monitor.Observe([Session(stage)], gameRunning: false);

        Assert.Equal(GameLifecycleAction.None, decision.Action);
    }

    [Fact]
    public void MultipleLocalSessionsNeverTransitionAutomatically()
    {
        var monitor = new GameLifecycleMonitor();

        var decision = monitor.Observe(
            [Session(TransferStage.InGame), Session(TransferStage.ReadyToPlay)],
            gameRunning: false);

        Assert.Equal(GameLifecycleAction.None, decision.Action);
    }

    private static TransferSession Session(TransferStage stage) =>
        TransferSession.Create(Guid.NewGuid(), "Steven", DateTimeOffset.UtcNow) with { Stage = stage };
}
