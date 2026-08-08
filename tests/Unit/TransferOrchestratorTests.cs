using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class TransferOrchestratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gsh-orchestrator-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartReachesAwaitingPlaceholder()
    {
        var (orchestrator, adapter, server, _) = CreateHarness();
        var result = await orchestrator.StartAsync(Guid.NewGuid(), "Stevenpwlk");

        Assert.True(result.Success);
        Assert.Equal(TransferStage.AwaitingPlaceholder, result.Session!.Stage);
        Assert.StartsWith("GSHIMPORT", result.Session!.PlaceholderName!, StringComparison.Ordinal);
        Assert.Equal(1, server.AcquireCalls);
        Assert.Equal(1, adapter.PrepareCalls);
        Assert.Equal(1, adapter.BaselineCalls);
    }

    [Fact]
    public async Task StartPlayerMissingAbortsServerBeforeImport()
    {
        var (orchestrator, adapter, server, _) = CreateHarness();
        adapter.PreparationOutcome = HostPreparationOutcome.PlayerNotFound;
        var result = await orchestrator.StartAsync(Guid.NewGuid(), "Absent");

        Assert.False(result.Success);
        Assert.Equal("player_not_found", result.Code);
        Assert.Equal(TransferStage.Failed, result.Session!.Stage);
        Assert.Equal(1, server.AbortCalls);
        Assert.Equal(0, adapter.ImportCalls);
    }

    [Fact]
    public async Task StartSecondActiveSessionIsRejected()
    {
        var (orchestrator, _, _, _) = CreateHarness();
        var first = await orchestrator.StartAsync(Guid.NewGuid(), "Stevenpwlk");
        var second = await orchestrator.StartAsync(Guid.NewGuid(), "Stevenpwlk");

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal("active_session_exists", second.Code);
    }

    [Fact]
    public async Task ConfirmPlaceholderImportsOnlyAfterServerImportStarting()
    {
        var (orchestrator, adapter, server, _) = CreateHarness();
        var started = await orchestrator.StartAsync(Guid.NewGuid(), "Stevenpwlk");
        var result = await orchestrator.ConfirmPlaceholderReadyAsync(started.Session!.LocalSessionId);

        Assert.True(result.Success);
        Assert.Equal(TransferStage.ReadyToPlay, result.Session!.Stage);
        Assert.True(result.Session!.ServerImportStarted);
        Assert.Equal(1, server.ImportStartingCalls);
        Assert.Equal(1, adapter.ImportCalls);
    }

    [Fact]
    public async Task RecoveryDuringImportWithPlaceholderDoesNotWriteAutomatically()
    {
        var (orchestrator, adapter, _, store) = CreateHarness();
        var started = await orchestrator.StartAsync(Guid.NewGuid(), "Stevenpwlk");
        var importing = started.Session! with
        {
            Stage = TransferStage.Importing,
            TargetLogicalName = "Standard-9.json",
            PlaceholderPayloadSha256 = "placeholder",
            ServerImportStarted = true
        };
        await store.WriteAsync(importing, "test-importing");
        adapter.ReconciliationState = ImportReconciliationState.PlaceholderIntact;

        var recovered = await orchestrator.RecoverAllAsync();

        Assert.Single(recovered);
        Assert.False(recovered[0].Success);
        Assert.Equal(TransferStage.Interrupted, recovered[0].Session!.Stage);
        Assert.Equal(TransferStage.Importing, recovered[0].Session!.ResumeStage);
        Assert.Equal(0, adapter.ImportCalls);
    }

    [Fact]
    public async Task RecoveryDuringImportWithArtifactPresentMarksReadyWithoutWrite()
    {
        var (orchestrator, adapter, _, store) = CreateHarness();
        var started = await orchestrator.StartAsync(Guid.NewGuid(), "Stevenpwlk");
        var importing = started.Session! with
        {
            Stage = TransferStage.Importing,
            TargetLogicalName = "Standard-9.json",
            PlaceholderPayloadSha256 = "placeholder",
            ServerImportStarted = true
        };
        await store.WriteAsync(importing, "test-importing");
        adapter.ReconciliationState = ImportReconciliationState.ImportedArtifactPresent;

        var recovered = await orchestrator.RecoverAllAsync();

        Assert.Single(recovered);
        Assert.True(recovered[0].Success);
        Assert.Equal(TransferStage.ReadyToPlay, recovered[0].Session!.Stage);
        Assert.Equal(0, adapter.ImportCalls);
    }

    [Fact]
    public async Task ExplicitResumeAfterPlaceholderReconciliationRetriesImport()
    {
        var (orchestrator, adapter, _, store) = CreateHarness();
        var started = await orchestrator.StartAsync(Guid.NewGuid(), "Stevenpwlk");
        var interrupted = started.Session! with
        {
            Stage = TransferStage.Interrupted,
            ResumeStage = TransferStage.Importing,
            TargetLogicalName = "Standard-9.json",
            PlaceholderPayloadSha256 = "placeholder",
            ServerImportStarted = true
        };
        await store.WriteAsync(interrupted, "test-interrupted");
        adapter.ReconciliationState = ImportReconciliationState.PlaceholderIntact;

        var result = await orchestrator.ResumeAsync(interrupted.LocalSessionId);

        Assert.True(result.Success);
        Assert.Equal(TransferStage.ReadyToPlay, result.Session!.Stage);
        Assert.Equal(1, adapter.ImportCalls);
    }

    [Fact]
    public async Task CompletePlayPublishesAndCompletes()
    {
        var (orchestrator, adapter, server, _) = CreateHarness();
        var started = await orchestrator.StartAsync(Guid.NewGuid(), "Stevenpwlk");
        var ready = await orchestrator.ConfirmPlaceholderReadyAsync(started.Session!.LocalSessionId);
        adapter.GameRunning = false;

        var result = await orchestrator.CompletePlayAsync(ready.Session!.LocalSessionId);

        Assert.True(result.Success);
        Assert.Equal(TransferStage.Completed, result.Session!.Stage);
        Assert.NotNull(result.Session!.ResultVersionId);
        Assert.True(server.PutChunkCalls > 0);
        Assert.Equal(1, server.CommitCalls);
    }

    [Fact]
    public async Task ResumePublishingReplaysKnownCommitWithoutCreatingUpload()
    {
        var (orchestrator, _, server, store) = CreateHarness();
        var session = TransferSession.Create(Guid.NewGuid(), "Stevenpwlk", DateTimeOffset.UtcNow);
        var sessionDirectory = store.GetSessionDirectory(session.LocalSessionId);
        var outbound = Path.Combine(sessionDirectory, "outbound.gshsave");
        await File.WriteAllBytesAsync(outbound, [1, 2, 3, 4, 5]);
        session = session with
        {
            ServerSessionId = Guid.NewGuid(),
            Stage = TransferStage.Publishing,
            OutboundArtifactPath = outbound,
            OutboundArtifactSha256 = "outbound-sha",
            OutboundArtifactLength = 5,
            UploadId = Guid.NewGuid()
        };
        await store.WriteAsync(session, "test-publishing");

        var result = await orchestrator.ResumeAsync(session.LocalSessionId);

        Assert.True(result.Success);
        Assert.Equal(TransferStage.Completed, result.Session!.Stage);
        Assert.NotNull(result.Session.ResultVersionId);
        Assert.Equal(0, server.CreateUploadCalls);
        Assert.Equal(1, server.CommitCalls);
    }

    [Fact]
    public async Task AbortAfterServerImportStartedIsForbidden()
    {
        var (orchestrator, _, server, _) = CreateHarness();
        var started = await orchestrator.StartAsync(Guid.NewGuid(), "Stevenpwlk");
        var ready = await orchestrator.ConfirmPlaceholderReadyAsync(started.Session!.LocalSessionId);

        var result = await orchestrator.AbortAsync(ready.Session!.LocalSessionId);

        Assert.False(result.Success);
        Assert.Equal("abort_forbidden", result.Code);
        Assert.Equal(0, server.AbortCalls);
    }

    [Fact]
    public async Task FileStoreRoundTripsSessionAndJournal()
    {
        Directory.CreateDirectory(_root);
        using var store = new FileTransferSessionStore(_root);
        var session = TransferSession.Create(Guid.NewGuid(), "Stevenpwlk", DateTimeOffset.UtcNow) with { Revision = 1 };

        await store.WriteAsync(session, "created");
        var loaded = await store.ReadAsync(session.LocalSessionId);

        Assert.NotNull(loaded);
        Assert.Equal(session.LocalSessionId, loaded!.LocalSessionId);
        Assert.True(File.Exists(Path.Combine(store.GetSessionDirectory(session.LocalSessionId), "events.ndjson")));
    }

    [Theory]
    [InlineData(TransferStage.Completed, true)]
    [InlineData(TransferStage.Aborted, true)]
    [InlineData(TransferStage.Failed, true)]
    [InlineData(TransferStage.ManualReview, false)]
    [InlineData(TransferStage.ReadyToPlay, false)]
    public void TerminalRulesAreConservative(TransferStage stage, bool expectedTerminal)
    {
        Assert.Equal(expectedTerminal, TransferStageRules.IsTerminal(stage));
    }

    private (TransferOrchestrator Orchestrator, FakeAdapter Adapter, FakeServer Server, MemoryStore Store) CreateHarness()
    {
        Directory.CreateDirectory(_root);
        var adapter = new FakeAdapter(_root);
        var server = new FakeServer(_root);
        var store = new MemoryStore(_root);
        return (new TransferOrchestrator(adapter, server, store), adapter, server, store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class MemoryStore(string root) : ITransferSessionStore
    {
        private readonly Dictionary<Guid, TransferSession> _sessions = [];
        public string RootPath { get; } = root;
        public string GetSessionDirectory(Guid localSessionId)
        {
            var path = Path.Combine(RootPath, localSessionId.ToString("D"));
            Directory.CreateDirectory(path);
            return path;
        }
        public Task<TransferSession?> ReadAsync(Guid localSessionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_sessions.GetValueOrDefault(localSessionId));
        }
        public Task<IReadOnlyList<TransferSession>> ReadActiveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<TransferSession>>(_sessions.Values.Where(session => TransferStageRules.HoldsLocalLock(session.Stage)).ToArray());
        }
        public Task<IReadOnlyList<TransferSession>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<TransferSession>>(_sessions.Values.ToArray());
        }
        public Task WriteAsync(TransferSession session, string eventName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
            _sessions[session.LocalSessionId] = session;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeServer(string root) : ITransferServerClient
    {
        private readonly string _artifactPath = Path.Combine(root, "server-source.gshsave");
        public int AcquireCalls { get; private set; }
        public int AbortCalls { get; private set; }
        public int ImportStartingCalls { get; private set; }
        public int CreateUploadCalls { get; private set; }
        public int PutChunkCalls { get; private set; }
        public int CommitCalls { get; private set; }
        private Guid _uploadId;

        public Task<IReadOnlyList<WorldCatalogItemResponse>> ListWorldsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorldCatalogItemResponse>>([
                new WorldCatalogItemResponse(Guid.NewGuid(), "Shlags1", "Available", Guid.NewGuid(), true)
            ]);

        public Task<WorldPreviewResponse> GetWorldPreviewAsync(Guid worldId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorldPreviewResponse(
                worldId,
                "Shlags1",
                "Available",
                Guid.NewGuid(),
                "artifact-sha",
                "planet-crafter-pc-gamepass",
                "Shlags1",
                569155654,
                [new WorldPreviewPlayerResponse(0, "Stevenpwlk", true, 3, 4)],
                true));

        public Task<WorldStatusResponse> GetWorldStatusAsync(Guid worldId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorldStatusResponse(worldId, "Shlags1", "Available", Guid.NewGuid(), null));

        public Task<AcquireWorldResponse> AcquireWorldAsync(Guid worldId, Guid? expectedVersionId, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            AcquireCalls++;
            return Task.FromResult(new AcquireWorldResponse(Guid.NewGuid(), expectedVersionId, "Preparing"));
        }

        public async Task<ServerArtifactDownload> DownloadSessionArtifactAsync(Guid serverSessionId, string destinationPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(_artifactPath, [1, 2, 3, 4], cancellationToken);
            File.Copy(_artifactPath, destinationPath, overwrite: true);
            return new ServerArtifactDownload(true, destinationPath, "source-sha", 4);
        }

        public Task MarkImportStartingAsync(Guid serverSessionId, CancellationToken cancellationToken = default)
        {
            ImportStartingCalls++;
            return Task.CompletedTask;
        }
        public Task HeartbeatAsync(Guid serverSessionId, string clientState, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CreateUploadResponse> CreateUploadAsync(Guid serverSessionId, CreateUploadRequest request, CancellationToken cancellationToken = default)
        {
            CreateUploadCalls++;
            _uploadId = Guid.NewGuid();
            return Task.FromResult(new CreateUploadResponse(_uploadId, 2, []));
        }
        public Task PutUploadChunkAsync(Guid uploadId, int index, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
        {
            Assert.Equal(_uploadId, uploadId);
            Assert.True(content.Length > 0);
            PutChunkCalls++;
            return Task.CompletedTask;
        }
        public Task<CommitUploadResponse> CommitUploadAsync(Guid uploadId, CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            return Task.FromResult(new CommitUploadResponse(Guid.NewGuid(), "outbound-sha", 5));
        }
        public Task AbortSessionAsync(Guid serverSessionId, CancellationToken cancellationToken = default)
        {
            AbortCalls++;
            return Task.CompletedTask;
        }
        public Task ReportFailureAsync(Guid serverSessionId, string code, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAdapter(string root) : IGameSaveAdapter
    {
        public string Id => "fake";
        public AdapterCapabilityReport Capabilities => new(true, true, true, true, true, false, "test");
        public HostPreparationOutcome PreparationOutcome { get; set; } = HostPreparationOutcome.Prepared;
        public ImportReconciliationState ReconciliationState { get; set; } = ImportReconciliationState.PlaceholderIntact;
        public int PrepareCalls { get; private set; }
        public int BaselineCalls { get; private set; }
        public int ImportCalls { get; private set; }
        public bool GameRunning { get; set; }

        public Task<InstallationDetection> DetectInstallationAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LocalStorageInspection> InspectLocalStorageAsync(CancellationToken cancellationToken = default)
        {
            var world = new DiscoveredWorld("Standard-9.json", "Shlags1", null, null, 1, "blob", [new DiscoveredPlayer(0, "Stevenpwlk", true, null, "0,0,0", 3, 4)]);
            return Task.FromResult(new LocalStorageInspection(1, Id, "pkg", DateTimeOffset.UtcNow, false, true, [], [], [world], []));
        }
        public Task<SnapshotResult> CreateSafetySnapshotAsync(string outputRoot, string? acknowledgedTestWorldName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string? ExportedByLogicalName { get; private set; }

        public async Task<PortableSaveArtifact> ExportPortableArtifactAsync(string worldName, string outputRoot, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputRoot);
            var path = Path.Combine(outputRoot, "outbound.gshsave");
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4, 5], cancellationToken);
            return new PortableSaveArtifact(path, "outbound-sha", 5, null);
        }

        public async Task<PortableSaveArtifact> ExportPortableArtifactByLogicalNameAsync(string logicalName, string outputRoot, CancellationToken cancellationToken = default)
        {
            ExportedByLogicalName = logicalName;
            return await ExportPortableArtifactAsync(logicalName, outputRoot, cancellationToken);
        }
        public Task<ArtifactValidation> ValidateArtifactAsync(PortableSaveArtifact artifact, CancellationToken cancellationToken = default) => Task.FromResult(new ArtifactValidation(true, []));
        public async Task<HostPreparation> PrepareForHostAsync(PortableSaveArtifact artifact, string playerName, string outputRoot, CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            if (PreparationOutcome != HostPreparationOutcome.Prepared)
            {
                return new HostPreparation(false, PreparationOutcome, null, playerName, null, null, false, ["guard"]);
            }
            Directory.CreateDirectory(outputRoot);
            var path = Path.Combine(outputRoot, "prepared.gshsave");
            await File.WriteAllBytesAsync(path, [5, 4, 3, 2, 1], cancellationToken);
            var manifest = new PortableArtifactManifest(1, Id, DateTimeOffset.UtcNow, "Standard-1.json", "Shlags1", null, null, 1, "payload/world.save", 5, "payload-sha", [new DiscoveredPlayer(0, playerName, true, null, "0,0,0", 3, 4)]);
            return new HostPreparation(true, HostPreparationOutcome.Prepared, new PortableSaveArtifact(path, "prepared-sha", 5, manifest), playerName, 7, 0, true, []);
        }
        public Task<ImportBaselineResult> CreateImportBaselineAsync(string outputRoot, CancellationToken cancellationToken = default)
        {
            BaselineCalls++;
            var path = Path.Combine(outputRoot, "baseline");
            Directory.CreateDirectory(path);
            return Task.FromResult(new ImportBaselineResult(true, path, null, []));
        }
        public Task<ImportTargetProbeResult> ProbeImportTargetAsync(string baselineDirectory, string expectedPlaceholderName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportTargetProbeResult(true, "Standard-9.json", expectedPlaceholderName, "placeholder", []));
        public Task<ImportReconciliationResult> ReconcilePortableImportAsync(PortableSaveArtifact artifact, string baselineDirectory, string expectedPlayerName, string targetLogicalName, string placeholderPayloadSha256, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportReconciliationResult(ReconciliationState, targetLogicalName, ReconciliationState == ImportReconciliationState.PlaceholderIntact ? placeholderPayloadSha256 : "payload-sha", "payload-sha", []));
        public Task<PortableImportResult> ImportPortableArtifactAsync(PortableSaveArtifact artifact, string baselineDirectory, string expectedPlayerName, string expectedPlaceholderName, string preImportBackupOutputRoot, CancellationToken cancellationToken = default)
        {
            ImportCalls++;
            return Task.FromResult(new PortableImportResult(true, "Standard-9.json", "Shlags1", Path.Combine(root, "snapshot"), "placeholder", "payload-sha", []));
        }
        public Task<GameLaunch> LaunchGameAsync(CancellationToken cancellationToken = default) => Task.FromResult(new GameLaunch(false, null, ["not used"]));
        public Task<GameProcessDetection> DetectGameProcessAsync(CancellationToken cancellationToken = default) => Task.FromResult(new GameProcessDetection(GameRunning, []));
        public Task<SaveStability> WaitForSaveStabilityAsync(TimeSpan observationWindow, CancellationToken cancellationToken = default) => Task.FromResult(new SaveStability(true, []));
    }
}
