using GameSaveHub.Server.Admin;

namespace GameSaveHub.UnitTests;

public sealed class WorldReplaceCommandParserTests
{
    [Fact]
    public void ParsesRequiredOptionsAndRepeatedPlayers()
    {
        var worldId = Guid.NewGuid();
        var currentVersionId = Guid.NewGuid();

        var parsed = WorldReplaceCommandParser.Parse(
        [
            "world", "replace", worldId.ToString("D"), "bob.gshsave", currentVersionId.ToString("D"),
            "--source-player", "BoB XiMe",
            "--require-player", "Stevenpwlk",
            "--require-player", "Maxdrake59",
            "--reason", "Sauvegarde actuelle de Bob"
        ]);

        Assert.Equal(worldId, parsed.WorldId);
        Assert.Equal(Path.GetFullPath("bob.gshsave"), parsed.ArtifactPath);
        Assert.Equal(currentVersionId, parsed.ExpectedCurrentVersionId);
        Assert.Equal("BoB XiMe", parsed.SourcePlayerName);
        Assert.Equal(["Stevenpwlk", "Maxdrake59"], parsed.RequiredPlayerNames);
        Assert.Equal("Sauvegarde actuelle de Bob", parsed.Reason);
    }

    [Theory]
    [InlineData("--source-player")]
    [InlineData("--reason")]
    [InlineData("--unknown")]
    public void RejectsMissingOrUnknownOptions(string brokenOption)
    {
        var args = new List<string>
        {
            "world", "replace", Guid.NewGuid().ToString("D"), "bob.gshsave", Guid.NewGuid().ToString("D"),
            "--source-player", "Bob",
            "--reason", "Justification valide"
        };
        if (brokenOption == "--unknown")
            args.AddRange(["--unknown", "value"]);
        else
            args.RemoveRange(args.IndexOf(brokenOption), 2);

        Assert.Throws<InvalidOperationException>(() => WorldReplaceCommandParser.Parse([.. args]));
    }
}
