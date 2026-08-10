using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class TransferTransitionGateTests
{
    [Fact]
    public async Task SerializesTwoMutatingTransitions()
    {
        using var gate = new TransferTransitionGate();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;

        var first = gate.RunAsync(async () =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
            return 1;
        });
        await firstEntered.Task;
        var second = gate.RunAsync(() =>
        {
            secondEntered = true;
            return Task.FromResult(2);
        });

        await Task.Delay(20);
        Assert.False(secondEntered);
        releaseFirst.SetResult();

        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
        Assert.True(secondEntered);
    }
}
