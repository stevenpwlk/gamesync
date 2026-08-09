using GameSaveHub.Adapters.Abstractions;
using GameSaveHub.Contracts;
using GameSaveHub.SaveExporter.Core;

namespace GameSaveHub.UnitTests;

public sealed class SaveExporterServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameSaveHubExporterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DiscoveryKeepsHomonymsDistinctAndUsesBlobModificationTime()
    {
        var firstWrite = new DateTimeOffset(2026, 8, 8, 18, 30, 0, TimeSpan.Zero);
        var secondWrite = firstWrite.AddHours(2);
        var adapter = new StubAdapter
        {
            Inspection = CreateInspection(
                [
                    CreateWorld("Standard-3.json", "Partie commune", "blob-a", "Bob"),
                    CreateWorld("Standard-4.json", "Partie commune", "blob-b", "Steven")
                ],
                [CreateFile("blob-a", firstWrite), CreateFile("blob-b", secondWrite)])
        };

        var discovered = await new SaveExporterService(adapter).DiscoverAsync();

        Assert.Collection(
            discovered,
            first =>
            {
                Assert.Equal("Standard-3.json", first.LogicalName);
                Assert.Equal("Partie commune", first.DisplayName);
                Assert.Equal(firstWrite, first.LastModifiedAtUtc);
                Assert.Equal("Bob", Assert.Single(first.Players).Name);
                Assert.Equal("Hôte", Assert.Single(first.Players).RoleLabel);
            },
            second =>
            {
                Assert.Equal("Standard-4.json", second.LogicalName);
                Assert.Equal(secondWrite, second.LastModifiedAtUtc);
                Assert.Equal("Steven", Assert.Single(second.Players).Name);
            });
    }

    [Fact]
    public async Task ExportUsesLogicalNameAndReturnsValidatedArtifact()
    {
        Directory.CreateDirectory(_root);
        var artifactPath = Path.Combine(_root, "chosen.gshsave");
        var adapter = new StubAdapter
        {
            ExportedArtifact = new PortableSaveArtifact(artifactPath, new string('a', 64), 3, null),
            ExportedBytes = [1, 2, 3],
            Validation = new ArtifactValidation(true, [])
        };

        var result = await new SaveExporterService(adapter).ExportAsync("Standard-4.json", _root);

        Assert.Equal("Standard-4.json", adapter.ExportedLogicalName);
        Assert.Equal(Path.GetFullPath(_root), adapter.ExportedDestination);
        Assert.Equal(artifactPath, result.Path);
    }

    [Fact]
    public async Task InvalidFinalArtifactIsDeletedWithoutTouchingOtherFiles()
    {
        Directory.CreateDirectory(_root);
        var artifactPath = Path.Combine(_root, "invalid.gshsave");
        var unrelatedPath = Path.Combine(_root, "keep.txt");
        await File.WriteAllTextAsync(unrelatedPath, "keep");
        var adapter = new StubAdapter
        {
            ExportedArtifact = new PortableSaveArtifact(artifactPath, new string('b', 64), 7, null),
            ExportedBytes = "invalid"u8.ToArray(),
            Validation = new ArtifactValidation(false, ["hash invalide"])
        };

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SaveExporterService(adapter).ExportAsync("Standard-4.json", _root));

        Assert.Contains("hash invalide", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(artifactPath));
        Assert.Equal("keep", await File.ReadAllTextAsync(unrelatedPath));
    }

    [Fact]
    public async Task ExportRejectsNetworkDestinationBeforeCallingAdapter()
    {
        var adapter = new StubAdapter();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SaveExporterService(adapter).ExportAsync("Standard-4.json", @"\\server\share"));

        Assert.Null(adapter.ExportedLogicalName);
    }

    private static LocalStorageInspection CreateInspection(
        IReadOnlyList<DiscoveredWorld> worlds,
        IReadOnlyList<DiagnosticFile> files) => new(
            1,
            "planet-crafter-pc-gamepass",
            "MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t",
            DateTimeOffset.UtcNow,
            false,
            true,
            [],
            files,
            worlds,
            []);

    private static DiscoveredWorld CreateWorld(string logicalName, string displayName, string blob, string player) => new(
        logicalName,
        displayName,
        null,
        "Standard",
        null,
        blob,
        [new DiscoveredPlayer(0, player, true, null, null, 1, 2)]);

    private static DiagnosticFile CreateFile(string path, DateTimeOffset modifiedAtUtc) => new(
        path,
        100,
        modifiedAtUtc,
        new string('c', 64),
        DiagnosticFileRole.OpaqueBlob,
        true);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class StubAdapter : IGameSaveAdapter
    {
        public string Id => "stub";
        public AdapterCapabilityReport Capabilities => new(true, false, true, false, false, false, "test");
        public LocalStorageInspection Inspection { get; init; } = CreateInspection([], []);
        public PortableSaveArtifact? ExportedArtifact { get; init; }
        public byte[]? ExportedBytes { get; init; }
        public ArtifactValidation Validation { get; init; } = new(true, []);
        public string? ExportedLogicalName { get; private set; }
        public string? ExportedDestination { get; private set; }

        public Task<LocalStorageInspection> InspectLocalStorageAsync(CancellationToken cancellationToken = default) => Task.FromResult(Inspection);

        public Task<PortableSaveArtifact> ExportPortableArtifactByLogicalNameAsync(
            string logicalName,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            ExportedLogicalName = logicalName;
            ExportedDestination = outputRoot;
            if (ExportedArtifact is not null && ExportedBytes is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ExportedArtifact.Path)!);
                File.WriteAllBytes(ExportedArtifact.Path, ExportedBytes);
            }
            return Task.FromResult(ExportedArtifact ?? throw new InvalidOperationException("Artefact de test absent."));
        }

        public Task<ArtifactValidation> ValidateArtifactAsync(PortableSaveArtifact artifact, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validation);

        public Task<InstallationDetection> DetectInstallationAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SnapshotResult> CreateSafetySnapshotAsync(string outputRoot, string? acknowledgedTestWorldName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PortableSaveArtifact> ExportPortableArtifactAsync(string worldName, string outputRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HostPreparation> PrepareForHostAsync(PortableSaveArtifact artifact, string playerName, string targetDisplayName, string outputRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportBaselineResult> CreateImportBaselineAsync(string outputRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportTargetProbeResult> ProbeImportTargetAsync(string baselineDirectory, string expectedPlaceholderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportReconciliationResult> ReconcilePortableImportAsync(PortableSaveArtifact artifact, string baselineDirectory, string expectedPlayerName, string targetLogicalName, string placeholderPayloadSha256, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PortableImportResult> ImportPortableArtifactAsync(PortableSaveArtifact artifact, string baselineDirectory, string expectedPlayerName, string expectedPlaceholderName, string preImportBackupOutputRoot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GameLaunch> LaunchGameAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GameProcessDetection> DetectGameProcessAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SaveStability> WaitForSaveStabilityAsync(TimeSpan observationWindow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
