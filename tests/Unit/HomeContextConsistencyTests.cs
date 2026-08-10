using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class HomeContextConsistencyTests
{
    [Fact]
    public void RefusesOwnServerLockWithoutLocalCheckpoint()
    {
        var deviceId = Guid.NewGuid();
        var status = Status(deviceId, Guid.NewGuid());

        var code = HomeContextConsistency.Validate(deviceId, status, null);

        Assert.Equal("local_checkpoint_missing", code);
    }

    [Fact]
    public void RefusesMismatchedLocalAndServerSessions()
    {
        var deviceId = Guid.NewGuid();
        var local = Local(Guid.NewGuid());
        var status = Status(deviceId, Guid.NewGuid());

        var code = HomeContextConsistency.Validate(deviceId, status, local);

        Assert.Equal("session_checkpoint_mismatch", code);
    }

    [Fact]
    public void AcceptsMatchingLocalAndServerSession()
    {
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var code = HomeContextConsistency.Validate(deviceId, Status(deviceId, sessionId), Local(sessionId));

        Assert.Null(code);
    }

    [Fact]
    public void AcceptsAnotherPlayersRemoteSessionWithoutLocalCheckpoint()
    {
        var code = HomeContextConsistency.Validate(Guid.NewGuid(), Status(Guid.NewGuid(), Guid.NewGuid()), null);

        Assert.Null(code);
    }

    private static TransferSession Local(Guid serverSessionId) =>
        TransferSession.Create(Guid.NewGuid(), "Steven", DateTimeOffset.UtcNow) with
        {
            ServerSessionId = serverSessionId,
            Stage = TransferStage.InGame
        };

    private static WorldStatusResponse Status(Guid deviceId, Guid sessionId) => new(
        Guid.NewGuid(),
        "Principal",
        "InGame",
        Guid.NewGuid(),
        sessionId,
        new(sessionId, deviceId, "Steven", "InGame", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        null);
}
