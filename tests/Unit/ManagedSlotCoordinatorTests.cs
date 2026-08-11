using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class ManagedSlotCoordinatorTests
{
    private const string Player = "Stevenpwlk";
    private const string Package = "pkg";

    [Fact]
    public async Task BindExistingSucceedsForSoleLegacyCandidateAndWritesOnlyTheBinding()
    {
        var (coordinator, adapter, slotStore, _, _) = CreateHarness();
        adapter.Inspection = InspectionWith(Legacy("Standard-5.json"));

        var result = await coordinator.BindExistingAsync(Player);

        Assert.True(result.Success);
        Assert.Equal("managed_slot_bound", result.Code);
        var binding = await slotStore.ReadAsync();
        Assert.NotNull(binding);
        Assert.Equal("Standard-5.json", binding!.LogicalName);
        Assert.Equal(ManagedSlotResolver.PermanentDisplayName, binding.DesiredDisplayName);
        Assert.Equal(2, adapter.InspectCalls);
    }

    [Fact]
    public async Task BindExistingSucceedsForSoleUnboundCandidateAndWritesOnlyTheBinding()
    {
        // UnboundCandidate : le monde porte déjà le nom permanent (par ex. après une
        // désinstallation complète Lot 3 suivie d'une réinstallation) — pas de renommage
        // à venir, donc CurrentDisplayName == DesiredDisplayName dès l'écriture.
        var (coordinator, adapter, slotStore, _, _) = CreateHarness();
        adapter.Inspection = InspectionWith(Ready("Standard-5.json"));

        var result = await coordinator.BindExistingAsync(Player);

        Assert.True(result.Success);
        Assert.Equal("managed_slot_bound", result.Code);
        var binding = await slotStore.ReadAsync();
        Assert.NotNull(binding);
        Assert.Equal("Standard-5.json", binding!.LogicalName);
        Assert.Equal(ManagedSlotResolver.PermanentDisplayName, binding.CurrentDisplayName);
        Assert.Equal(ManagedSlotResolver.PermanentDisplayName, binding.DesiredDisplayName);
        Assert.Equal(2, adapter.InspectCalls);
    }

    [Fact]
    public async Task BindExistingRefusesWhenAlreadyBound()
    {
        var (coordinator, adapter, slotStore, _, _) = CreateHarness();
        adapter.Inspection = InspectionWith(Legacy("Standard-5.json"));
        await slotStore.WriteAsync(Binding("Standard-1.json"));

        var result = await coordinator.BindExistingAsync(Player);

        Assert.False(result.Success);
        Assert.Equal("managed_slot_already_bound", result.Code);
    }

    [Fact]
    public async Task BindExistingRefusesTwoLegacyCandidates()
    {
        var (coordinator, adapter, slotStore, _, _) = CreateHarness();
        adapter.Inspection = InspectionWith(Legacy("Standard-5.json"), Legacy("Standard-6.json"));

        var result = await coordinator.BindExistingAsync(Player);

        Assert.False(result.Success);
        Assert.Null(await slotStore.ReadAsync());
    }

    [Fact]
    public async Task BindExistingRefusesWhileGameIsRunning()
    {
        var (coordinator, adapter, slotStore, _, _) = CreateHarness();
        adapter.Inspection = InspectionWith(Legacy("Standard-5.json"));
        adapter.GameRunning = true;

        var result = await coordinator.BindExistingAsync(Player);

        Assert.False(result.Success);
        Assert.Equal("game_running", result.Code);
        Assert.Null(await slotStore.ReadAsync());
    }

    [Fact]
    public async Task BindExistingRefusesWhileATransferSessionIsActive()
    {
        var (coordinator, adapter, slotStore, sessionStore, _) = CreateHarness();
        adapter.Inspection = InspectionWith(Legacy("Standard-5.json"));
        sessionStore.Active = [TransferSession.Create(Guid.NewGuid(), Player, DateTimeOffset.UtcNow)];

        var result = await coordinator.BindExistingAsync(Player);

        Assert.False(result.Success);
        Assert.Equal("active_session_exists", result.Code);
        Assert.Null(await slotStore.ReadAsync());
    }

    [Fact]
    public async Task GetStatusReflectsCurrentBindingAndInspection()
    {
        var (coordinator, adapter, slotStore, _, _) = CreateHarness();
        await slotStore.WriteAsync(Binding("Standard-5.json"));
        adapter.Inspection = InspectionWith(Ready("Standard-5.json"));

        var resolution = await coordinator.GetStatusAsync(Player);

        Assert.Equal(ManagedSlotStatus.Ready, resolution.Status);
    }

    [Fact]
    public async Task MaintenanceStatusIsSafeWhenIdle()
    {
        var (coordinator, _, _, _, _) = CreateHarness();

        var status = await coordinator.GetMaintenanceStatusAsync();

        Assert.True(status.SafeToUpdate);
    }

    [Fact]
    public async Task MaintenanceStatusIsUnsafeWhileGameRunsOrTransitionIsBusy()
    {
        var (coordinator, adapter, _, _, gate) = CreateHarness();
        adapter.GameRunning = true;

        var duringGame = await coordinator.GetMaintenanceStatusAsync();
        Assert.False(duringGame.SafeToUpdate);
        Assert.False(duringGame.GameClosed);

        adapter.GameRunning = false;
        var busyTask = gate.RunAsync(async () =>
        {
            var status = await coordinator.GetMaintenanceStatusAsync();
            Assert.False(status.TransitionIdle);
            Assert.False(status.SafeToUpdate);
            return true;
        });
        Assert.True(await busyTask);
    }

    private static (ManagedSlotCoordinator Coordinator, FakeAdapter Adapter, FakeManagedSlotStore SlotStore, FakeSessionStore SessionStore, TransferTransitionGate Gate) CreateHarness()
    {
        var adapter = new FakeAdapter();
        var slotStore = new FakeManagedSlotStore();
        var sessionStore = new FakeSessionStore();
        var gate = new TransferTransitionGate();
        return (new ManagedSlotCoordinator(adapter, slotStore, sessionStore, gate), adapter, slotStore, sessionStore, gate);
    }

    private static ManagedSlotBinding Binding(string logicalName) => ManagedSlotBinding.Create(
        "fake", Package, Player, logicalName, ManagedSlotResolver.PermanentDisplayName, ManagedSlotResolver.PermanentDisplayName, DateTimeOffset.UtcNow);

    private static DiscoveredWorld Legacy(string logicalName) =>
        new(logicalName, "GSH-SHLAGS-RETURN", null, null, 1, "blob", [new DiscoveredPlayer(0, Player, true, null, "0,0,0", 3, 4)]);

    private static DiscoveredWorld Ready(string logicalName) =>
        new(logicalName, ManagedSlotResolver.PermanentDisplayName, null, null, 1, "blob", [new DiscoveredPlayer(0, Player, true, null, "0,0,0", 3, 4)]);

    private static LocalStorageInspection InspectionWith(params DiscoveredWorld[] worlds) =>
        new(1, "fake", Package, DateTimeOffset.UtcNow, false, true, [], [], worlds, []);

    private sealed class FakeManagedSlotStore : IManagedSlotStore
    {
        private ManagedSlotBinding? _binding;
        public Task<ManagedSlotBinding?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_binding);
        public Task WriteAsync(ManagedSlotBinding binding, CancellationToken cancellationToken = default)
        {
            _binding = binding;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionStore : ITransferSessionStore
    {
        public IReadOnlyList<TransferSession> Active { get; set; } = [];
        public bool IsWriteInProgress { get; set; }
        public string RootPath => throw new NotSupportedException();
        public string GetSessionDirectory(Guid localSessionId) => throw new NotSupportedException();
        public Task<TransferSession?> ReadAsync(Guid localSessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TransferSession>> ReadActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Active);
        public Task<IReadOnlyList<TransferSession>> ReadAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WriteAsync(TransferSession session, string eventName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeAdapter : IGameSaveAdapter
    {
        public string Id => "fake";
        public AdapterCapabilityReport Capabilities => new(true, true, true, true, true, false, "test");
        public LocalStorageInspection Inspection { get; set; } = InspectionWith();
        public bool GameRunning { get; set; }
        public int InspectCalls { get; private set; }

        public Task<InstallationDetection> DetectInstallationAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LocalStorageInspection> InspectLocalStorageAsync(CancellationToken cancellationToken = default)
        {
            InspectCalls++;
            return Task.FromResult(Inspection);
        }
        public Task<SnapshotResult> CreateSafetySnapshotAsync(string outputRoot, string? acknowledgedTestWorldName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PortableSaveArtifact> ExportPortableArtifactAsync(string worldName, string outputRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PortableSaveArtifact> ExportPortableArtifactByLogicalNameAsync(string logicalName, string outputRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ArtifactValidation> ValidateArtifactAsync(PortableSaveArtifact artifact, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HostPreparation> PrepareForHostAsync(PortableSaveArtifact artifact, string playerName, string targetDisplayName, string outputRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportBaselineResult> CreateImportBaselineAsync(string outputRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportTargetProbeResult> ProbeImportTargetAsync(string baselineDirectory, string expectedPlaceholderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportReconciliationResult> ReconcilePortableImportAsync(PortableSaveArtifact artifact, string baselineDirectory, string expectedPlayerName, string targetLogicalName, string placeholderPayloadSha256, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PortableImportResult> ImportPortableArtifactAsync(PortableSaveArtifact artifact, string baselineDirectory, string expectedPlayerName, string expectedPlaceholderName, string preImportBackupOutputRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GameLaunch> LaunchGameAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GameProcessDetection> DetectGameProcessAsync(CancellationToken cancellationToken = default) => Task.FromResult(new GameProcessDetection(GameRunning, []));
        public Task<SaveStability> WaitForSaveStabilityAsync(TimeSpan observationWindow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
