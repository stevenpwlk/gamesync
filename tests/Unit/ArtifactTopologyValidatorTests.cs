using GameSaveHub.Server.Infrastructure;

namespace GameSaveHub.UnitTests;

public sealed class ArtifactTopologyValidatorTests
{
    [Fact]
    public void AcceptsSingleHostZeroAndRequiredPlayersExactlyOnce()
    {
        var summary = CreateSummary(
            new ArtifactEnvelopePlayerSummary(0, "BoB XiMe", true, 5, 6),
            new ArtifactEnvelopePlayerSummary(7, "Stevenpwlk", false, 3, 4));

        ArtifactTopologyValidator.Validate(summary, [" bob xime ", "STEVENPWLK"]);
    }

    [Theory]
    [MemberData(nameof(InvalidTopologies))]
    public void RejectsUnsafeTopology(IReadOnlyList<ArtifactEnvelopePlayerSummary> players, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ArtifactTopologyValidator.Validate(CreateSummary([.. players]), []));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMissingOrAmbiguousRequiredPlayer()
    {
        var missing = CreateSummary(new ArtifactEnvelopePlayerSummary(0, "Bob", true, 1, 2));
        var ambiguous = CreateSummary(
            new ArtifactEnvelopePlayerSummary(0, "Bob", true, 1, 2),
            new ArtifactEnvelopePlayerSummary(4, " bob ", false, 3, 4));

        Assert.Contains(
            "Steven",
            Assert.Throws<InvalidOperationException>(() => ArtifactTopologyValidator.Validate(missing, ["Steven"])).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "exactement une fois",
            Assert.Throws<InvalidOperationException>(() => ArtifactTopologyValidator.Validate(ambiguous, ["BOB"])).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<IReadOnlyList<ArtifactEnvelopePlayerSummary>, string> InvalidTopologies => new()
    {
        {
            [
                new ArtifactEnvelopePlayerSummary(0, "Bob", true, 1, 2),
                new ArtifactEnvelopePlayerSummary(0, "Steven", false, 3, 4)
            ],
            "identifiants"
        },
        {
            [
                new ArtifactEnvelopePlayerSummary(0, "Bob", false, 1, 2),
                new ArtifactEnvelopePlayerSummary(7, "Steven", true, 3, 4)
            ],
            "hôte"
        },
        {
            [
                new ArtifactEnvelopePlayerSummary(0, "Bob", true, 1, 2),
                new ArtifactEnvelopePlayerSummary(7, "Steven", false, 1, 4)
            ],
            "inventaire"
        },
        {
            [
                new ArtifactEnvelopePlayerSummary(0, "Bob", true, 1, 2),
                new ArtifactEnvelopePlayerSummary(7, "Steven", false, 3, 2)
            ],
            "équipement"
        }
    };

    private static ArtifactEnvelopeSummary CreateSummary(params ArtifactEnvelopePlayerSummary[] players) => new(
        1,
        "planet-crafter-pc-gamepass",
        "Partie commune",
        42,
        12,
        new string('a', 64),
        players);
}
