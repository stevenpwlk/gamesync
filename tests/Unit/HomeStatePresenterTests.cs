using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class HomeStatePresenterTests
{
    public static TheoryData<HomeContextSnapshot, HomeVisualState, HomePrimaryAction> Cases => new()
    {
        { Context(enrolled: false), HomeVisualState.Onboarding, HomePrimaryAction.None },
        { Context(serverHealthy: false), HomeVisualState.Unavailable, HomePrimaryAction.None },
        { Context(safetyStopCode: "multiple_primary_worlds"), HomeVisualState.SafetyStop, HomePrimaryAction.OpenDiagnostics },
        { Context(wgsStable: false), HomeVisualState.SafetyStop, HomePrimaryAction.None },
        { Context(wgsAvailable: false), HomeVisualState.ReadyToPlay, HomePrimaryAction.LaunchGame },
        { Context(gameRunning: true), HomeVisualState.OffHub, HomePrimaryAction.None },
        { Context(), HomeVisualState.Ready, HomePrimaryAction.StartTransfer },
        { Context(localStage: TransferStage.Acquiring), HomeVisualState.Preparing, HomePrimaryAction.None },
        { Context(localStage: TransferStage.AwaitingPlaceholder), HomeVisualState.Placeholder, HomePrimaryAction.LaunchGame },
        { Context(localStage: TransferStage.ReadyToPlay), HomeVisualState.ReadyToPlay, HomePrimaryAction.LaunchGame },
        { Context(localStage: TransferStage.InGame, gameRunning: true), HomeVisualState.Hosting, HomePrimaryAction.None },
        { Context(localStage: TransferStage.CapturingResult), HomeVisualState.Securing, HomePrimaryAction.None },
        { Context(localStage: TransferStage.Interrupted), HomeVisualState.Interrupted, HomePrimaryAction.ResumeTransfer },
        { Context(localStage: TransferStage.ManualReview), HomeVisualState.ManualReview, HomePrimaryAction.OpenDiagnostics },
        { Context(remoteState: "Preparing", remotePlayer: "Bob"), HomeVisualState.RemotePreparing, HomePrimaryAction.None },
        { Context(remoteState: "InGame", remotePlayer: "Bob"), HomeVisualState.RemoteHosting, HomePrimaryAction.LaunchGame },
        { Context(remoteState: "InGame", remotePlayer: "Bob", gameRunning: true), HomeVisualState.RemoteHosting, HomePrimaryAction.None }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ProducesExpectedStateAndSinglePrimaryAction(
        HomeContextSnapshot context,
        HomeVisualState expectedState,
        HomePrimaryAction expectedAction)
    {
        var view = HomeStatePresenter.Present(context);

        Assert.Equal(expectedState, view.State);
        Assert.Equal(expectedAction, view.PrimaryAction);
        Assert.False(string.IsNullOrWhiteSpace(view.Title));
        Assert.False(string.IsNullOrWhiteSpace(view.Instruction));
        Assert.Equal(expectedAction != HomePrimaryAction.None, !string.IsNullOrWhiteSpace(view.PrimaryActionLabel));
    }

    [Fact]
    public void NamesRemoteHostWithoutExposingDeviceIdentity()
    {
        var view = HomeStatePresenter.Present(Context(remoteState: "InGame", remotePlayer: "Bob"));

        Assert.Contains("Bob", view.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("PC", view.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsesNeutralWordingForLegacyRemoteSessionWithoutPlayerName()
    {
        var view = HomeStatePresenter.Present(Context(remoteState: "InGame", remotePlayer: null));

        Assert.Contains("Un autre joueur", view.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotExposeTechnicalErrorDetailsOnInterruptedHomeState()
    {
        var context = Context(localStage: TransferStage.Interrupted);
        context = context with
        {
            LocalSession = context.LocalSession! with { LastErrorMessage = @"C:\Users\Steven\private-save.json" }
        };

        var view = HomeStatePresenter.Present(context);

        Assert.DoesNotContain("C:\\Users", view.Instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostic", view.Instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingSlotAndFreeWorldOffersOneTimeConfiguration()
    {
        var view = HomeStatePresenter.Present(Context(slot: ManagedSlotStatus.Missing));

        Assert.Equal("Configurons ce PC", view.Title);
        Assert.Equal(HomePrimaryAction.ConfigureManagedSlot, view.PrimaryAction);
        Assert.Equal("Configurer ce PC", view.PrimaryActionLabel);
    }

    [Fact]
    public void ReadySlotAndFreeWorldKeepsDailyTakeControlAction()
    {
        var view = HomeStatePresenter.Present(Context(slot: ManagedSlotStatus.Ready));

        Assert.Equal("Le monde est prêt", view.Title);
        Assert.Equal(HomePrimaryAction.StartTransfer, view.PrimaryAction);
        Assert.Equal("Prendre la main", view.PrimaryActionLabel);
    }

    [Fact]
    public void RenamePendingBehavesLikeReadyForDailyTakeControl()
    {
        var view = HomeStatePresenter.Present(Context(slot: ManagedSlotStatus.RenamePending));

        Assert.Equal("Le monde est prêt", view.Title);
        Assert.Equal(HomePrimaryAction.StartTransfer, view.PrimaryAction);
        Assert.Equal("Prendre la main", view.PrimaryActionLabel);
    }

    [Theory]
    [InlineData(ManagedSlotStatus.LegacyCandidate)]
    [InlineData(ManagedSlotStatus.UnboundCandidate)]
    public void CandidateStatusesOfferExplicitRebind(ManagedSlotStatus status)
    {
        // UnboundCandidate se produit désormais légitimement après une désinstallation
        // complète (Lot 3) suivie d'une réinstallation : le monde GSH-MONDE-PARTAGE existe
        // déjà, mais ce PC n'a plus de managed-slot.json local. ResolveCandidate garantit
        // les mêmes vérifications de topologie (hôte unique, id 0, joueur correspondant)
        // que pour LegacyCandidate ; le rattacher exige toujours un clic explicite du joueur,
        // jamais automatique.
        var view = HomeStatePresenter.Present(Context(slot: status));

        Assert.Equal(HomeVisualState.ManagedSlotRebind, view.State);
        Assert.Equal(HomePrimaryAction.BindExistingManagedSlot, view.PrimaryAction);
    }

    [Theory]
    [InlineData(ManagedSlotStatus.BoundSlotMissing)]
    [InlineData(ManagedSlotStatus.BindingMismatch)]
    [InlineData(ManagedSlotStatus.InvalidTopology)]
    [InlineData(ManagedSlotStatus.Ambiguous)]
    public void InconsistentSlotStatusEntersRepairStop(ManagedSlotStatus status)
    {
        var view = HomeStatePresenter.Present(Context(slot: status));

        Assert.Equal("Le slot du monde doit être vérifié", view.Title);
        Assert.Equal(HomePrimaryAction.OpenDiagnostics, view.PrimaryAction);
    }

    [Fact]
    public void RemoteHostTakesPriorityOverLocalSlotSetup()
    {
        var view = HomeStatePresenter.Present(Context(
            slot: ManagedSlotStatus.Missing,
            remoteState: "InGame",
            remotePlayer: "Bob"));

        Assert.Equal(HomeVisualState.RemoteHosting, view.State);
        Assert.Equal(HomePrimaryAction.LaunchGame, view.PrimaryAction);
        Assert.Equal("Lancer The Planet Crafter", view.PrimaryActionLabel);
    }

    [Fact]
    public void GameOutsideHubTakesPriorityOverLocalSlotSetup()
    {
        var view = HomeStatePresenter.Present(Context(slot: ManagedSlotStatus.Missing, gameRunning: true));

        Assert.Equal(HomeVisualState.OffHub, view.State);
        Assert.Equal(HomePrimaryAction.None, view.PrimaryAction);
    }

    [Fact]
    public void InitialSlotSetupStepOneOffersExactCopyText()
    {
        var context = Context(localStage: TransferStage.AwaitingPlaceholder, flowKind: TransferFlowKind.InitialSlotSetup);

        var view = HomeStatePresenter.Present(context);

        Assert.Equal("Configuration unique — étape 1 sur 2", view.Title);
        Assert.True(view.ShowCopySlotName);
        Assert.Equal("GSH-MONDE-PARTAGE", view.SlotName);
        Assert.Equal(HomePrimaryAction.LaunchGame, view.PrimaryAction);
    }

    [Fact]
    public void InitialSlotSetupInstallingShowsNoAction()
    {
        var context = Context(localStage: TransferStage.Importing, flowKind: TransferFlowKind.InitialSlotSetup);

        var view = HomeStatePresenter.Present(context);

        Assert.Equal("Installation du monde partagé…", view.Title);
        Assert.Equal(HomePrimaryAction.None, view.PrimaryAction);
        Assert.False(view.ShowCopySlotName);
    }

    [Fact]
    public void InitialSlotSetupStepTwoOffersLaunch()
    {
        var context = Context(localStage: TransferStage.ReadyToPlay, flowKind: TransferFlowKind.InitialSlotSetup);

        var view = HomeStatePresenter.Present(context);

        Assert.Equal("Configuration unique — étape 2 sur 2", view.Title);
        Assert.Equal(HomePrimaryAction.LaunchGame, view.PrimaryAction);
        Assert.False(view.ShowCopySlotName);
    }

    [Fact]
    public void DailyReuseAwaitingImportKeepsUnchangedWording()
    {
        var context = Context(localStage: TransferStage.ReadyToPlay, flowKind: TransferFlowKind.ManagedSlotReuse);

        var view = HomeStatePresenter.Present(context);

        Assert.Equal("Tout est prêt pour jouer", view.Title);
        Assert.False(view.ShowCopySlotName);
    }

    [Fact]
    public void SelectsOnlyUniqueWorldWithArtifact()
    {
        var selected = PrimaryWorldSelector.Select(
        [
            new WorldCatalogItemResponse(Guid.NewGuid(), "Vide", "Available", null, false),
            new WorldCatalogItemResponse(Guid.NewGuid(), "Principal", "Available", Guid.NewGuid(), true)
        ]);

        Assert.True(selected.Success);
        Assert.Equal("Principal", selected.World!.Name);
    }

    [Theory]
    [InlineData(0, "primary_world_missing")]
    [InlineData(2, "multiple_primary_worlds")]
    public void RefusesAmbiguousPrimaryWorld(int artifactCount, string expectedCode)
    {
        var worlds = Enumerable.Range(0, artifactCount)
            .Select(index => new WorldCatalogItemResponse(Guid.NewGuid(), $"Monde {index}", "Available", Guid.NewGuid(), true))
            .ToArray();

        var selected = PrimaryWorldSelector.Select(worlds);

        Assert.False(selected.Success);
        Assert.Equal(expectedCode, selected.Code);
        Assert.Null(selected.World);
    }

    private static HomeContextSnapshot Context(
        bool enrolled = true,
        bool serverHealthy = true,
        bool gameRunning = false,
        string? safetyStopCode = null,
        bool wgsStable = true,
        bool wgsAvailable = true,
        TransferStage? localStage = null,
        TransferFlowKind flowKind = TransferFlowKind.LegacyPlaceholder,
        string? remoteState = null,
        string? remotePlayer = null,
        ManagedSlotStatus slot = ManagedSlotStatus.Ready)
    {
        var deviceId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var local = localStage is TransferStage stage
            ? TransferSession.Create(worldId, "Steven", DateTimeOffset.UtcNow, flowKind) with { Stage = stage }
            : null;
        var remote = remoteState is null
            ? null
            : new ActiveWorldSessionResponse(
                Guid.NewGuid(),
                Guid.NewGuid(),
                remotePlayer,
                remoteState,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow);
        var status = new WorldStatusResponse(
            worldId,
            "Monde principal",
            remoteState ?? "Available",
            Guid.NewGuid(),
            remote?.SessionId,
            remote,
            null);
        return new HomeContextSnapshot(
            enrolled,
            enrolled ? deviceId : null,
            enrolled ? "Steven" : null,
            serverHealthy,
            new WorldCatalogItemResponse(worldId, "Monde principal", status.Status, status.CurrentVersionId, true),
            status,
            safetyStopCode,
            local,
            null,
            gameRunning,
            wgsStable,
            wgsAvailable,
            slot);
    }
}
