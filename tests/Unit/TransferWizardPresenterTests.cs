using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class TransferWizardPresenterTests
{
    private static TransferSession SessionAt(TransferStage stage, bool importStarted = false) =>
        TransferSession.Create(Guid.NewGuid(), "Stevenpwlk", DateTimeOffset.UnixEpoch) with
        {
            Stage = stage,
            PlaceholderName = "GSHIMPORTA1B2C3",
            ServerImportStarted = importStarted
        };

    [Fact]
    public void IdleWithoutLocalGateOffersNoAction()
    {
        var view = TransferWizardPresenter.Describe(null, preflightCompatible: true, wgsTransferEnabled: false);

        Assert.Null(view.PrimaryAction);
        Assert.False(view.ShowAbort);
    }

    [Fact]
    public void IdleWithoutPreflightOffersNoAction()
    {
        var view = TransferWizardPresenter.Describe(null, preflightCompatible: false, wgsTransferEnabled: true);

        Assert.Null(view.PrimaryAction);
    }

    [Fact]
    public void IdleReadyOffersStart()
    {
        var view = TransferWizardPresenter.Describe(null, preflightCompatible: true, wgsTransferEnabled: true);

        Assert.Equal(TransferWizardPresenter.StartCommand, view.PrimaryAction!.Command);
    }

    [Theory]
    [InlineData(TransferStage.AwaitingPlaceholder, TransferWizardPresenter.PlaceholderReadyCommand)]
    [InlineData(TransferStage.ReadyToPlay, TransferWizardPresenter.PlayStartedCommand)]
    [InlineData(TransferStage.InGame, TransferWizardPresenter.PlayCompleteCommand)]
    [InlineData(TransferStage.Interrupted, TransferWizardPresenter.ResumeCommand)]
    public void EachInteractiveStageOffersExactlyItsCommand(TransferStage stage, string expected)
    {
        var view = TransferWizardPresenter.Describe(SessionAt(stage), true, true);

        Assert.Equal(expected, view.PrimaryAction!.Command);
        Assert.False(view.IsWaitingOnService);
    }

    [Theory]
    [InlineData(TransferStage.Initialized)]
    [InlineData(TransferStage.Acquiring)]
    [InlineData(TransferStage.DownloadingArtifact)]
    [InlineData(TransferStage.PreparingArtifact)]
    [InlineData(TransferStage.CreatingBaseline)]
    [InlineData(TransferStage.Importing)]
    [InlineData(TransferStage.CapturingResult)]
    [InlineData(TransferStage.UploadPending)]
    [InlineData(TransferStage.Uploading)]
    [InlineData(TransferStage.Publishing)]
    public void AutomaticStagesOfferNoActionAndAreWaiting(TransferStage stage)
    {
        var view = TransferWizardPresenter.Describe(SessionAt(stage), true, true);

        Assert.Null(view.PrimaryAction);
        Assert.True(view.IsWaitingOnService);
    }

    [Fact]
    public void AwaitingPlaceholderSurfacesTheNameToCopy()
    {
        var view = TransferWizardPresenter.Describe(SessionAt(TransferStage.AwaitingPlaceholder), true, true);

        Assert.Equal("GSHIMPORTA1B2C3", view.PlaceholderName);
        Assert.NotEmpty(view.Steps);
    }

    [Fact]
    public void ManualReviewOffersNoAutomaticAction()
    {
        var session = SessionAt(TransferStage.ManualReview, importStarted: true) with
        {
            LastErrorCode = "protected_world_changed",
            LastErrorMessage = "Un monde protégé a changé."
        };

        var view = TransferWizardPresenter.Describe(session, true, true);

        Assert.Null(view.PrimaryAction);
        Assert.False(view.ShowAbort);
        Assert.Equal(WizardTone.Danger, view.Tone);
        Assert.Contains("protected_world_changed", view.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void AbortDisappearsOnceServerImportStarted()
    {
        var before = TransferWizardPresenter.Describe(SessionAt(TransferStage.AwaitingPlaceholder), true, true);
        var after = TransferWizardPresenter.Describe(SessionAt(TransferStage.AwaitingPlaceholder, importStarted: true), true, true);

        Assert.True(before.ShowAbort);
        Assert.False(after.ShowAbort);
    }

    [Fact]
    public void ImportingNeverOffersAbort()
    {
        var view = TransferWizardPresenter.Describe(SessionAt(TransferStage.Importing), true, true);

        Assert.False(view.ShowAbort);
    }

    [Theory]
    [InlineData(TransferStage.Completed)]
    [InlineData(TransferStage.Aborted)]
    [InlineData(TransferStage.Failed)]
    public void TerminalStagesOfferOnlyALocalDismissal(TransferStage stage)
    {
        var view = TransferWizardPresenter.Describe(SessionAt(stage), true, true);

        Assert.NotNull(view.PrimaryAction);
        Assert.Null(view.PrimaryAction!.Command);
        Assert.False(view.IsWaitingOnService);
    }

    [Fact]
    public void CompletedShowsThePublishedVersion()
    {
        var versionId = Guid.NewGuid();
        var session = SessionAt(TransferStage.Completed) with { ResultVersionId = versionId };

        var view = TransferWizardPresenter.Describe(session, true, true);

        Assert.Equal(WizardTone.Success, view.Tone);
        Assert.Contains(versionId.ToString(), view.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStageProducesATitleAndAnInstruction()
    {
        foreach (var stage in Enum.GetValues<TransferStage>())
        {
            var view = TransferWizardPresenter.Describe(SessionAt(stage), true, true);

            Assert.False(string.IsNullOrWhiteSpace(view.Title), $"Titre manquant pour {stage}.");
            Assert.False(string.IsNullOrWhiteSpace(view.Instruction), $"Instruction manquante pour {stage}.");
        }
    }
}
