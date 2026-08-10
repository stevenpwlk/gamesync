using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class ClientCompatibilityPolicyTests
{
    [Theory]
    [InlineData(null, "0.4.0", false)]
    [InlineData("0.3.9", "0.4.0", false)]
    [InlineData("0.4.0", "0.4.0", true)]
    [InlineData("0.4.1", "0.4.0", true)]
    public void AcquireCompatibilityIsDeterministic(string? client, string minimum, bool allowed) =>
        Assert.Equal(allowed, ClientCompatibilityPolicy.CanAcquire(client, minimum));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyMinimumIsNonConstraining(string? minimum)
    {
        Assert.True(ClientCompatibilityPolicy.CanAcquire(null, minimum));
        Assert.True(ClientCompatibilityPolicy.CanAcquire("abc", minimum));
        Assert.True(ClientCompatibilityPolicy.CanAcquire("0.1.0", minimum));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.4")]
    [InlineData("0.4.0.1")]
    [InlineData("abc")]
    public void MalformedOrMissingClientVersionIsRejectedWhenMinimumIsConfigured(string? client) =>
        Assert.False(ClientCompatibilityPolicy.CanAcquire(client, "0.4.0"));

    [Fact]
    public void MalformedConfiguredMinimumIsTreatedAsNonConstraining() =>
        Assert.True(ClientCompatibilityPolicy.CanAcquire(null, "not-a-version"));
}
