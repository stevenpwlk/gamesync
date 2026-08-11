using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class UpdateDecisionTests
{
    [Fact]
    public void NewerManifestVersionIsApplied() =>
        Assert.True(UpdateDecision.ShouldApplyUpdate("0.5.0", "0.5.1"));

    [Fact]
    public void EqualVersionIsRefused() =>
        Assert.False(UpdateDecision.ShouldApplyUpdate("0.5.0", "0.5.0"));

    [Fact]
    public void OlderManifestVersionIsRefused() =>
        Assert.False(UpdateDecision.ShouldApplyUpdate("0.6.0", "0.5.9"));

    [Fact]
    public void OlderMajorVersionIsRefusedEvenWithHigherMinor() =>
        Assert.False(UpdateDecision.ShouldApplyUpdate("1.0.0", "0.99.0"));

    [Fact]
    public void ComparisonIsNumericNotLexicographic() =>
        Assert.True(UpdateDecision.ShouldApplyUpdate("0.9.0", "0.10.0"));

    [Fact]
    public void MissingInstalledVersionMeansFirstRunAndApplies() =>
        Assert.True(UpdateDecision.ShouldApplyUpdate(null, "0.5.0"));

    [Fact]
    public void BlankInstalledVersionAppliesLikeAMissingOne() =>
        Assert.True(UpdateDecision.ShouldApplyUpdate("   ", "0.5.0"));

    /// <summary>
    /// Fichier VERSION corrompu : on ne peut plus nommer la version en place, donc pas la
    /// comparer. Appliquer est le choix documenté — refuser bloquerait le poste pour toujours.
    /// </summary>
    [Fact]
    public void UnreadableInstalledVersionApplies() =>
        Assert.True(UpdateDecision.ShouldApplyUpdate("0.5.0-preview", "0.6.0"));

    /// <summary>
    /// Version publiée incomparable : refus. Une bascule de dossier ne se déclenche jamais
    /// sur une version dont on ne sait pas prouver qu'elle est plus récente.
    /// </summary>
    [Fact]
    public void UnreadableManifestVersionIsRefused() =>
        Assert.False(UpdateDecision.ShouldApplyUpdate("0.5.0", "0.6.0-rc1"));

    [Fact]
    public void UnreadableManifestVersionIsRefusedEvenOnFirstRun() =>
        Assert.False(UpdateDecision.ShouldApplyUpdate(null, "latest"));

    [Fact]
    public void SurroundingWhitespaceInTheVersionFileIsTolerated() =>
        Assert.True(UpdateDecision.ShouldApplyUpdate(" 0.5.0\r\n", "0.5.1"));

    [Fact]
    public void TwoComponentVersionsCompare() =>
        Assert.True(UpdateDecision.ShouldApplyUpdate("0.5", "0.6"));

    [Fact]
    public void MissingStatusDefersBecauseSafetyIsUnknown() =>
        Assert.True(UpdateDecision.ShouldDeferUpdate(null));

    [Fact]
    public void UnsafeStatusDefers() =>
        Assert.True(UpdateDecision.ShouldDeferUpdate(new MaintenanceSafetyStatus(
            GameClosed: false,
            NoActiveTransfer: true,
            TransitionIdle: true,
            CheckpointDurable: true,
            SafeToUpdate: false)));

    [Fact]
    public void SafeStatusDoesNotDefer() =>
        Assert.False(UpdateDecision.ShouldDeferUpdate(new MaintenanceSafetyStatus(
            GameClosed: true,
            NoActiveTransfer: true,
            TransitionIdle: true,
            CheckpointDurable: true,
            SafeToUpdate: true)));
}
