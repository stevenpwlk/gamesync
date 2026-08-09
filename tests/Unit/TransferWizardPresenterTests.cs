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

    /// <summary>
    /// Constater qu'on est bloqué ne suffit pas : l'écran doit dire quoi faire.
    /// Sans cela le joueur reste devant un refus qu'il ne peut pas lever seul.
    /// </summary>
    [Fact]
    public void ClosedLocalGateNamesTheInstallerThatOpensIt()
    {
        var view = TransferWizardPresenter.Describe(null, preflightCompatible: true, wgsTransferEnabled: false);

        Assert.Contains("INSTALLER-GAMESAVEHUB-PILOTE.cmd", view.Instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedTransferRemainsVisibleOnceTheSessionIsNoLongerActive()
    {
        var finished = SessionAt(TransferStage.Completed) with
        {
            ResultVersionId = Guid.Parse("fe32692b-c894-4a3f-baa1-add5c9bab87b")
        };

        var view = TransferWizardPresenter.Describe(null, preflightCompatible: true, wgsTransferEnabled: true, finished);

        Assert.NotNull(view.LastOutcome);
        Assert.Contains("fe32692b", view.LastOutcome!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedTransferSurfacesItsReasonInTheLastOutcome()
    {
        var finished = SessionAt(TransferStage.Failed) with
        {
            LastErrorCode = "capture_failed",
            LastErrorMessage = "Plusieurs mondes portent ce nom."
        };

        var outcome = TransferWizardPresenter.DescribeLastOutcome(finished);

        Assert.Contains("capture_failed", outcome!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Une session encore en cours n'est pas un résultat : l'afficher comme tel
    /// laisserait croire que le transfert est fini alors qu'il tourne encore.
    /// </summary>
    [Theory]
    [InlineData(TransferStage.Importing)]
    [InlineData(TransferStage.Interrupted)]
    [InlineData(TransferStage.InGame)]
    public void NonTerminalSessionsProduceNoLastOutcome(TransferStage stage)
    {
        Assert.Null(TransferWizardPresenter.DescribeLastOutcome(SessionAt(stage)));
    }

    [Fact]
    public void NoPreviousTransferProducesNoLastOutcome()
    {
        var view = TransferWizardPresenter.Describe(null, preflightCompatible: true, wgsTransferEnabled: true);

        Assert.Null(view.LastOutcome);
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

    [Theory]
    [InlineData(TransferStage.Acquiring, 1)]
    [InlineData(TransferStage.AwaitingPlaceholder, 2)]
    [InlineData(TransferStage.Importing, 3)]
    [InlineData(TransferStage.ReadyToPlay, 4)]
    [InlineData(TransferStage.InGame, 4)]
    [InlineData(TransferStage.Uploading, 5)]
    [InlineData(TransferStage.Completed, 6)]
    public void NominalStagesAreNumberedForTheUser(TransferStage stage, int expected)
    {
        var view = TransferWizardPresenter.Describe(SessionAt(stage), true, true);

        Assert.Equal(expected, view.StepNumber);
        Assert.Equal(TransferWizardPresenter.NominalStepCount, view.StepCount);
    }

    [Theory]
    [InlineData(TransferStage.Interrupted)]
    [InlineData(TransferStage.ManualReview)]
    [InlineData(TransferStage.Aborted)]
    [InlineData(TransferStage.Failed)]
    public void OffNominalStagesShowNoStepNumber(TransferStage stage)
    {
        var view = TransferWizardPresenter.Describe(SessionAt(stage), true, true);

        Assert.Equal(0, view.StepNumber);
    }

    [Fact]
    public void StepNumbersNeverGoBackwardsAlongTheNominalPath()
    {
        TransferStage[] path =
        [
            TransferStage.Initialized,
            TransferStage.Acquiring,
            TransferStage.DownloadingArtifact,
            TransferStage.PreparingArtifact,
            TransferStage.CreatingBaseline,
            TransferStage.AwaitingPlaceholder,
            TransferStage.Importing,
            TransferStage.ReadyToPlay,
            TransferStage.InGame,
            TransferStage.CapturingResult,
            TransferStage.UploadPending,
            TransferStage.Uploading,
            TransferStage.Publishing,
            TransferStage.Completed
        ];

        var previous = 0;
        foreach (var stage in path)
        {
            var step = TransferWizardPresenter.Describe(SessionAt(stage), true, true).StepNumber;
            Assert.True(step >= previous, $"L'étape recule à {stage} : {step} après {previous}.");
            Assert.InRange(step, 1, TransferWizardPresenter.NominalStepCount);
            previous = step;
        }
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
