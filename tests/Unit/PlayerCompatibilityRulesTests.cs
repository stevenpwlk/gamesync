using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class PlayerCompatibilityRulesTests
{
    private static readonly WorldPreviewPlayerResponse[] Players =
    [
        new(0, "BoB XiMe", true, 5, 6),
        new(4, "Maxdrake59", false, 7, 8),
        new(7, "Stevenpwlk", false, 3, 4)
    ];

    [Theory]
    [InlineData("Stevenpwlk")]
    [InlineData(" stevenpwlk ")]
    [InlineData("STEVENPWLK")]
    public void ExistingPlayerIsCompatible(string configuredName)
    {
        var result = PlayerCompatibilityRules.Evaluate(configuredName, Players);

        Assert.True(result.Compatible);
        Assert.Equal(PlayerCompatibilityOutcome.Compatible, result.Outcome);
        Assert.Equal("Stevenpwlk", result.MatchedPlayerName);
        Assert.Equal(1, result.MatchCount);
    }

    [Fact]
    public void MissingPlayerIsRejected()
    {
        var result = PlayerCompatibilityRules.Evaluate("Absent", Players);

        Assert.False(result.Compatible);
        Assert.Equal(PlayerCompatibilityOutcome.PlayerNotFound, result.Outcome);
        Assert.Equal(0, result.MatchCount);
    }

    [Fact]
    public void EmptyConfiguredNameIsRejected()
    {
        var result = PlayerCompatibilityRules.Evaluate("   ", Players);

        Assert.False(result.Compatible);
        Assert.Equal(PlayerCompatibilityOutcome.PlayerNameMissing, result.Outcome);
    }

    [Fact]
    public void DuplicateEquivalentNamesAreRejected()
    {
        WorldPreviewPlayerResponse[] ambiguous =
        [
            new(0, "Stevenpwlk", true, 3, 4),
            new(7, " STEVENPWLK ", false, 5, 6)
        ];

        var result = PlayerCompatibilityRules.Evaluate("Stevenpwlk", ambiguous);

        Assert.False(result.Compatible);
        Assert.Equal(PlayerCompatibilityOutcome.PlayerAmbiguous, result.Outcome);
        Assert.Equal(2, result.MatchCount);
    }
}
