namespace GameSaveHub.UnitTests;

public sealed class DiagnosticApplicationTests
{
    [Fact]
    public void PrepareHostDefaultsPermanentDisplayNameWhenOptionIsAbsent()
    {
        var displayName = DiagnosticApplication.ResolvePrepareHostDisplayName(
        [
            "--artifact", "source.gshsave",
            "--player", "Stevenpwlk",
            "--output", "prepared"
        ]);

        Assert.Equal("GSH-MONDE-PARTAGE", displayName);
    }

    [Fact]
    public void PrepareHostKeepsExplicitDisplayNameForDiagnostics()
    {
        var displayName = DiagnosticApplication.ResolvePrepareHostDisplayName(
        [
            "--artifact", "source.gshsave",
            "--player", "Stevenpwlk",
            "--display-name", "DIAGNOSTIC-SLOT",
            "--output", "prepared"
        ]);

        Assert.Equal("DIAGNOSTIC-SLOT", displayName);
    }
}
