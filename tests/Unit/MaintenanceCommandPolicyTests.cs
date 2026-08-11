using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class MaintenanceCommandPolicyTests
{
    [Fact]
    public void MaintenanceStatusIsAllowed() =>
        Assert.True(MaintenanceCommandPolicy.IsAllowedForLocalSystem("maintenance-status"));

    [Fact]
    public void CaseAndWhitespaceDoNotChangeTheAnswer() =>
        Assert.True(MaintenanceCommandPolicy.IsAllowedForLocalSystem("  Maintenance-Status "));

    [Theory]
    [InlineData("enroll")]
    [InlineData("transfer-start")]
    [InlineData("managed-slot-bind-existing")]
    [InlineData("home-context")]
    [InlineData("diagnostic-report")]
    [InlineData("status")]
    public void EverythingElseIsRefused(string command) =>
        Assert.False(MaintenanceCommandPolicy.IsAllowedForLocalSystem(command));

    [Fact]
    public void MissingCommandIsRefused() =>
        Assert.False(MaintenanceCommandPolicy.IsAllowedForLocalSystem(null));
}
