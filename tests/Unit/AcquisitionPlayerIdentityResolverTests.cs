using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class AcquisitionPlayerIdentityResolverTests
{
    private static readonly WorldPreviewPlayerResponse[] Players =
    [
        new(0, "Steven", true, 1, 2),
        new(7, "BoB XiMe", false, 3, 4)
    ];

    [Fact]
    public void ReturnsCanonicalManifestSpelling()
    {
        var result = AcquisitionPlayerIdentityResolver.Resolve("  bob xime ", Players);

        Assert.True(result.Success);
        Assert.Equal("BoB XiMe", result.CanonicalPlayerName);
    }

    [Fact]
    public void LegacyMissingPlayerNameRemainsAcceptedWithoutIdentity()
    {
        var result = AcquisitionPlayerIdentityResolver.Resolve(null, Players);

        Assert.True(result.Success);
        Assert.Null(result.CanonicalPlayerName);
    }

    [Fact]
    public void RejectsMissingPlayer()
    {
        var result = AcquisitionPlayerIdentityResolver.Resolve("Alice", Players);

        Assert.False(result.Success);
        Assert.Equal("player_not_found", result.Code);
    }

    [Fact]
    public void RejectsAmbiguousNormalizedPlayer()
    {
        var ambiguous = Players.Append(new(8, "bob xime", false, 5, 6)).ToArray();

        var result = AcquisitionPlayerIdentityResolver.Resolve("Bob Xime", ambiguous);

        Assert.False(result.Success);
        Assert.Equal("player_ambiguous", result.Code);
    }
}
