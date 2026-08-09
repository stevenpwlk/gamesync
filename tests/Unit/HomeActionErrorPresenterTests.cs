using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class HomeActionErrorPresenterTests
{
    [Fact]
    public void UnknownFailureNeverDisplaysRawServiceMessage()
    {
        var message = HomeActionErrorPresenter.Present("capture_failed");

        Assert.DoesNotContain("capture_failed", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostic", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("active_transfer_exists")]
    [InlineData("client_update_required")]
    [InlineData("player_not_found")]
    public void KnownFailuresUseCuratedUserFacingWording(string code)
    {
        var message = HomeActionErrorPresenter.Present(code);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain(code, message, StringComparison.OrdinalIgnoreCase);
    }
}
