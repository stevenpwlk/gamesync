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
    [InlineData("game_running")]
    [InlineData("wgs_not_stable")]
    [InlineData("managed_slot_already_bound")]
    [InlineData("managed_slot_candidate_changed")]
    [InlineData("managed_slot_requires_attention")]
    public void KnownFailuresUseCuratedUserFacingWording(string code)
    {
        var message = HomeActionErrorPresenter.Present(code);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain(code, message, StringComparison.OrdinalIgnoreCase);
    }
}
