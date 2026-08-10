using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class ServiceAccountGuardTests
{
    [Theory]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-19")]
    [InlineData("S-1-5-20")]
    public void ReservedAccountsAreRejected(string sid) =>
        Assert.True(ServiceAccountGuard.IsReservedAccount(sid));

    [Theory]
    [InlineData("S-1-5-21-111111111-222222222-333333333-1001")]
    [InlineData("")]
    public void OrdinaryAccountsAreAllowed(string sid) =>
        Assert.False(ServiceAccountGuard.IsReservedAccount(sid));
}
