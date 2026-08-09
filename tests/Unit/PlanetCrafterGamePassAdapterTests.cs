using GameSaveHub.Adapters.PlanetCrafter.GamePass;
using GameSaveHub.Contracts;
using System.IO.Compression;

namespace GameSaveHub.UnitTests;

public sealed class PlanetCrafterGamePassAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gsh-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task InspectClassifiesWgsFilesWithoutReadingOutsidePackage()
    {
        var wgs = CreateWgs();
        await File.WriteAllTextAsync(Path.Combine(wgs, "containers.index"), "index");
        var container = Directory.CreateDirectory(Path.Combine(wgs, "ABC")).FullName;
        await File.WriteAllTextAsync(Path.Combine(container, "container.1"), "metadata");
        await File.WriteAllTextAsync(Path.Combine(container, "0123456789ABCDEF0123456789ABCDEF"), "payload");

        var result = await CreateAdapter().InspectLocalStorageAsync();

        Assert.True(result.Stable);
        Assert.Contains(result.Files, file => file.Role == DiagnosticFileRole.ContainerIndex);
        Assert.Contains(result.Files, file => file.Role == DiagnosticFileRole.ContainerMetadata);
        Assert.Contains(result.Files, file => file.Role == DiagnosticFileRole.OpaqueBlob);
        Assert.All(result.Files, file => Assert.DoesNotContain(_root.Replace('\\', '/'), file.RelativePath));
    }

    [Fact]
    public async Task SnapshotRequiresExplicitTestWorldAcknowledgement()
    {
        CreateWgs();

        var result = await CreateAdapter().CreateSafetySnapshotAsync(Path.Combine(_root, "snapshots"), null);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("monde test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SnapshotRefusesWhileGameIsRunning()
    {
        CreateWgs();
        var adapter = CreateAdapter(() => [(42, "Planet Crafter")]);

        var result = await adapter.CreateSafetySnapshotAsync(Path.Combine(_root, "snapshots"), "Shlags1");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("Fermez", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SnapshotCopiesAndHashesStableFiles()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");

        var result = await CreateAdapter().CreateSafetySnapshotAsync(Path.Combine(_root, "snapshots"), "Shlags1");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.NotNull(result.Manifest);
        Assert.True(File.Exists(Path.Combine(result.SnapshotDirectory!, "snapshot-manifest.json")));
        Assert.Contains(result.Manifest!.Files, file => file.Role == DiagnosticFileRole.OpaqueBlob);
    }

    [Fact]
    public void ValidatedPilotCapabilitiesExposePreparationImportAndXboxLaunch()
    {
        var adapter = CreateAdapter();

        Assert.True(adapter.Capabilities.CanPrepareForHost);
        Assert.True(adapter.Capabilities.CanImportPortableArtifact);
        Assert.True(adapter.Capabilities.CanLaunchGame);
        Assert.Contains("production-gate", adapter.Capabilities.GateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchUsesInstalledXboxApplicationAumid()
    {
        string? activated = null;
        var adapter = new PlanetCrafterGamePassAdapter(new PlanetCrafterGamePassOptions
        {
            InstalledPackageFamilyProbe = () => PlanetCrafterGamePassOptions.DefaultPackageFamilyName,
            InstalledApplicationIdProbe = () => "Game",
            AppActivator = aumid => { activated = aumid; return null; },
            ProcessProbe = () => [(42, "Planet Crafter")],
            LaunchVerificationAttempts = 1,
            LaunchVerificationInterval = TimeSpan.Zero
        });

        var result = await adapter.LaunchGameAsync();

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(42, result.ProcessId);
        Assert.Equal($"{PlanetCrafterGamePassOptions.DefaultPackageFamilyName}!Game", activated);
    }

    [Fact]
    public async Task LaunchRefusesWhenXboxPackageIsAbsent()
    {
        var adapter = new PlanetCrafterGamePassAdapter(new PlanetCrafterGamePassOptions
        {
            InstalledPackageFamilyProbe = () => null,
            AppActivator = _ => throw new InvalidOperationException("must not activate")
        });

        var result = await adapter.LaunchGameAsync();

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("Xbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LaunchReportsActivationRefusalWithoutThrowing()
    {
        var adapter = new PlanetCrafterGamePassAdapter(new PlanetCrafterGamePassOptions
        {
            InstalledPackageFamilyProbe = () => PlanetCrafterGamePassOptions.DefaultPackageFamilyName,
            InstalledApplicationIdProbe = () => "Game",
            AppActivator = _ => throw new InvalidOperationException("activation refusée")
        });

        var result = await adapter.LaunchGameAsync();

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("Xbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LaunchRefusesSuccessWhenGameProcessNeverAppears()
    {
        var adapter = new PlanetCrafterGamePassAdapter(new PlanetCrafterGamePassOptions
        {
            InstalledPackageFamilyProbe = () => PlanetCrafterGamePassOptions.DefaultPackageFamilyName,
            InstalledApplicationIdProbe = () => "Game",
            AppActivator = _ => null,
            ProcessProbe = () => [],
            LaunchVerificationAttempts = 1,
            LaunchVerificationInterval = TimeSpan.Zero
        });

        var result = await adapter.LaunchGameAsync();

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("pas démarré", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiagnosticRestoreReplacesOnlyResolvedTestWorldBlob()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var unrelated = Path.Combine(wgs, "unrelated.bin");
        await File.WriteAllTextAsync(unrelated, "must-stay-unchanged");
        var adapter = CreateAdapter(activeNetworkRoute: false);
        var source = await adapter.CreateSafetySnapshotAsync(Path.Combine(_root, "source"), "Shlags1");
        Assert.True(source.Success);
        var blob = Directory.EnumerateFiles(wgs, "*", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(path).Length == 32);
        var sourceContent = await File.ReadAllTextAsync(blob);
        await File.WriteAllTextAsync(blob, sourceContent.Replace("1,2,3", "9,9,9", StringComparison.Ordinal));

        var result = await adapter.RestoreTestWorldFromSnapshotAsync(
            source.SnapshotDirectory!, "Shlags1", Path.Combine(_root, "pre-restore"), true);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(sourceContent, await File.ReadAllTextAsync(blob));
        Assert.Equal("must-stay-unchanged", await File.ReadAllTextAsync(unrelated));
        Assert.NotNull(result.PreRestoreSnapshotDirectory);
    }

    [Fact]
    public async Task DiagnosticRestoreRefusesActiveNetworkRoute()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var offlineAdapter = CreateAdapter(activeNetworkRoute: false);
        var source = await offlineAdapter.CreateSafetySnapshotAsync(Path.Combine(_root, "source"), "Shlags1");
        var onlineAdapter = CreateAdapter(activeNetworkRoute: true);

        var result = await onlineAdapter.RestoreTestWorldFromSnapshotAsync(
            source.SnapshotDirectory!, "Shlags1", Path.Combine(_root, "pre-restore"), true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("route réseau", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportByDisplayNameRefusesTwoHomonymousWorlds()
    {
        // Situation normale dès qu'une même sauvegarde a été importée deux fois sur
        // le même PC : deux mondes distincts portent le même nom affiché.
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-3.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-4.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.ExportPortableArtifactAsync("GSH-SHLAGS-RETURN", Path.Combine(_root, "artifacts")));

        Assert.Contains("Standard-3.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Standard-4.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportByLogicalNameDiscriminatesHomonymousWorlds()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-3.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-4.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();

        var artifact = await adapter.ExportPortableArtifactByLogicalNameAsync(
            "Standard-4.json", Path.Combine(_root, "artifacts"));

        Assert.NotNull(artifact.Manifest);
        Assert.Equal("Standard-4.json", artifact.Manifest.LogicalName);
        Assert.True((await adapter.ValidateArtifactAsync(artifact)).IsValid);
    }

    [Fact]
    public async Task PortableExportRefusesWhileGameIsRunning()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var output = Path.Combine(_root, "artifacts");
        var adapter = CreateAdapter(() => [(42, "Planet Crafter")]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.ExportPortableArtifactByLogicalNameAsync("Standard-2.json", output));

        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task PortableExportRefusesDestinationInsideWgs()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAdapter().ExportPortableArtifactByLogicalNameAsync(
                "Standard-2.json",
                Path.Combine(wgs, "exports")));

        Assert.False(Directory.Exists(Path.Combine(wgs, "exports")));
    }

    [Fact]
    public async Task PortableExportRefusesDestinationLinkingIntoWgs()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var linkedOutput = Path.Combine(_root, "linked-export");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAdapter(finalPathResolver: path =>
                    path.Equals(linkedOutput, StringComparison.OrdinalIgnoreCase) ? wgs : path)
                .ExportPortableArtifactByLogicalNameAsync("Standard-2.json", linkedOutput));
    }

    [Fact]
    public async Task PortableExportRefusesLocalLinkResolvingToNetworkShare()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var linkedOutput = Path.Combine(_root, "linked-network-export");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAdapter(finalPathResolver: path =>
                    path.Equals(linkedOutput, StringComparison.OrdinalIgnoreCase) ? @"\\server\share" : path)
                .ExportPortableArtifactByLogicalNameAsync("Standard-2.json", linkedOutput));

        Assert.False(Directory.Exists(linkedOutput));
    }

    [Fact]
    public async Task PortableExportRemovesPartialFileWhenGameStartsDuringCopy()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var calls = 0;
        var output = Path.Combine(_root, "artifacts");
        var adapter = CreateAdapter(() => ++calls >= 3 ? [(42, "Planet Crafter")] : []);

        await Assert.ThrowsAsync<IOException>(() =>
            adapter.ExportPortableArtifactByLogicalNameAsync("Standard-2.json", output));

        Assert.Empty(Directory.EnumerateFiles(output));
    }

    [Fact]
    public async Task PortableExportRemovesPartialFileWhenWorldChangesDuringCopy()
    {
        var wgs = CreateWgs();
        var blob = CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var calls = 0;
        var output = Path.Combine(_root, "artifacts");
        var adapter = CreateAdapter(() =>
        {
            if (++calls == 3) File.AppendAllText(blob, "mutation");
            return [];
        });

        await Assert.ThrowsAsync<IOException>(() =>
            adapter.ExportPortableArtifactByLogicalNameAsync("Standard-2.json", output));

        Assert.Empty(Directory.EnumerateFiles(output));
    }

    [Fact]
    public async Task PortableExportContainsOnlyManifestAndWorldPayload()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var adapter = CreateAdapter();

        var artifact = await adapter.ExportPortableArtifactAsync("Shlags1", Path.Combine(_root, "artifacts"));
        var validation = await adapter.ValidateArtifactAsync(artifact);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.NotNull(artifact.Manifest);
        Assert.Equal("Standard-2.json", artifact.Manifest.LogicalName);
        using var archive = ZipFile.OpenRead(artifact.Path);
        Assert.Equal(["manifest.json", "payload/world.save"], archive.Entries.Select(entry => entry.FullName).Order().ToArray());
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("container.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PortableValidationRejectsUnexpectedArchiveEntry()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("Shlags1", Path.Combine(_root, "artifacts"));
        using (var archive = ZipFile.Open(artifact.Path, ZipArchiveMode.Update))
        {
            archive.CreateEntry("../outside.txt");
        }

        var validation = await adapter.ValidateArtifactAsync(artifact);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("exactement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrepareForHostRejectsPlayerMissingFromSave()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "Shlags1", players:
        [
            new TestPlayer(0, "Stevenpwlk", true, 3, 4, "1,2,3"),
            new TestPlayer(4, "Maxdrake59", false, 7, 8, "4,5,6"),
            new TestPlayer(7, "BoB XiMe", false, 5, 6, "7,8,9")
        ]);
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("Shlags1", Path.Combine(_root, "artifacts"));

        var result = await adapter.PrepareForHostAsync(artifact, "UnknownPlayer", "Shlags1", Path.Combine(_root, "prepared"));

        Assert.False(result.Success);
        Assert.Equal(HostPreparationOutcome.PlayerNotFound, result.Outcome);
        Assert.Null(result.PreparedArtifact);
        Assert.Contains(result.Errors, error => error.Contains("n'existe pas", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrepareForHostSetsPermanentDisplayNameInPayloadAndManifest()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "Shlags1", players:
        [
            new TestPlayer(0, "Stevenpwlk", true, 3, 4, "1,2,3"),
            new TestPlayer(7, "BoB XiMe", false, 5, 6, "7,8,9")
        ]);
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("Shlags1", Path.Combine(_root, "artifacts"));

        var prepared = await adapter.PrepareForHostAsync(
            artifact,
            "Stevenpwlk",
            "GSH-MONDE-PARTAGE",
            Path.Combine(_root, "prepared"));

        Assert.True(prepared.Success, string.Join(Environment.NewLine, prepared.Errors));
        Assert.Equal("GSH-MONDE-PARTAGE", prepared.PreparedArtifact!.Manifest!.DisplayName);
        Assert.True((await adapter.ValidateArtifactAsync(prepared.PreparedArtifact)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\rname")]
    [InlineData("bad\nname")]
    public async Task PrepareForHostRejectsUnsafeDisplayName(string name)
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "Shlags1", players:
        [
            new TestPlayer(0, "Stevenpwlk", true, 3, 4, "1,2,3")
        ]);
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("Shlags1", Path.Combine(_root, "artifacts"));

        var prepared = await adapter.PrepareForHostAsync(
            artifact,
            "Stevenpwlk",
            name,
            Path.Combine(_root, "prepared"));

        Assert.False(prepared.Success);
        Assert.Equal(HostPreparationOutcome.InvalidDisplayName, prepared.Outcome);
        Assert.Contains("invalid_target_display_name", prepared.Errors);
        Assert.Null(prepared.PreparedArtifact);
    }

    [Fact]
    public async Task PrepareForHostRejectsAmbiguousNormalizedPlayerName()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "Shlags1", players:
        [
            new TestPlayer(0, "Alex", true, 3, 4, "1,2,3"),
            new TestPlayer(4, " alex ", false, 7, 8, "4,5,6")
        ]);
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("Shlags1", Path.Combine(_root, "artifacts"));

        var result = await adapter.PrepareForHostAsync(artifact, "ALEX", "Shlags1", Path.Combine(_root, "prepared"));

        Assert.False(result.Success);
        Assert.Equal(HostPreparationOutcome.PlayerAmbiguous, result.Outcome);
    }

    [Fact]
    public async Task PrepareForHostSwapsOnlyLocalPlayerIdentityFieldsAndPreservesPlayerDataLinks()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "Shlags1", players:
        [
            new TestPlayer(0, "Stevenpwlk", true, 3, 4, "10,20,30"),
            new TestPlayer(4, "Maxdrake59", false, 7, 8, "40,50,60"),
            new TestPlayer(7, "BoB XiMe", false, 5, 6, "70,80,90")
        ]);
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("Shlags1", Path.Combine(_root, "artifacts"));

        var result = await adapter.PrepareForHostAsync(artifact, "bob xime", "Shlags1", Path.Combine(_root, "prepared"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(HostPreparationOutcome.Prepared, result.Outcome);
        Assert.True(result.Changed);
        Assert.Equal(7, result.TargetPlayerOriginalId);
        Assert.NotNull(result.PreparedArtifact);
        var validation = await adapter.ValidateArtifactAsync(result.PreparedArtifact!);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        var manifest = Assert.IsType<PortableArtifactManifest>(result.PreparedArtifact!.Manifest);
        var bob = manifest.Players.Single(player => player.Name == "BoB XiMe");
        var steven = manifest.Players.Single(player => player.Name == "Stevenpwlk");
        var max = manifest.Players.Single(player => player.Name == "Maxdrake59");
        Assert.Equal((0, true, 5, 6, "70,80,90"), (bob.Id, bob.IsHost, bob.InventoryId, bob.EquipmentId, bob.Position));
        Assert.Equal((7, false, 3, 4, "10,20,30"), (steven.Id, steven.IsHost, steven.InventoryId, steven.EquipmentId, steven.Position));
        Assert.Equal((4, false, 7, 8, "40,50,60"), (max.Id, max.IsHost, max.InventoryId, max.EquipmentId, max.Position));
    }

    [Fact]
    public async Task PrepareForHostIsNoOpWhenRequestedPlayerAlreadyOwnsIdZero()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "Shlags1", players:
        [
            new TestPlayer(0, "Stevenpwlk", true, 3, 4, "1,2,3"),
            new TestPlayer(7, "BoB XiMe", false, 5, 6, "7,8,9")
        ]);
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("Shlags1", Path.Combine(_root, "artifacts"));

        var result = await adapter.PrepareForHostAsync(artifact, "Stevenpwlk", "Shlags1", Path.Combine(_root, "prepared"));

        Assert.True(result.Success);
        Assert.Equal(HostPreparationOutcome.AlreadyHost, result.Outcome);
        Assert.False(result.Changed);
        Assert.NotNull(result.PreparedArtifact);
        Assert.Equal(artifact.Manifest!.PayloadSha256, result.PreparedArtifact!.Manifest!.PayloadSha256);
    }

    [Fact]
    public async Task PrepareForHostRejectsTopologyWhereHostIsNotIdZero()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "Shlags1", players:
        [
            new TestPlayer(0, "Stevenpwlk", false, 3, 4, "1,2,3"),
            new TestPlayer(7, "BoB XiMe", true, 5, 6, "7,8,9")
        ]);
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("Shlags1", Path.Combine(_root, "artifacts"));

        var result = await adapter.PrepareForHostAsync(artifact, "BoB XiMe", "Shlags1", Path.Combine(_root, "prepared"));

        Assert.False(result.Success);
        Assert.Equal(HostPreparationOutcome.InvalidPlayerTopology, result.Outcome);
    }

    [Fact]
    public async Task ImportBaselineProtectsExistingWorldsAndImportTargetsOnlyNewStandardSlot()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "SourceWorld", players:
        [
            new TestPlayer(0, "Stevenpwlk", true, 3, 4, "1,2,3"),
            new TestPlayer(7, "BoB XiMe", false, 5, 6, "7,8,9")
        ], seed: 99);
        var adapter = CreateAdapter(activeNetworkRoute: true);
        var artifact = await adapter.ExportPortableArtifactAsync("SourceWorld", Path.Combine(_root, "artifacts"));
        var baseline = await adapter.CreateImportBaselineAsync(Path.Combine(_root, "baseline"));
        Assert.True(baseline.Success, string.Join(Environment.NewLine, baseline.Errors));
        var sourceBefore = (await adapter.InspectLocalStorageAsync()).Worlds.Single(world => world.LogicalName == "Standard-1.json");
        var sourceBeforeHash = (await adapter.InspectLocalStorageAsync()).Files.Single(file => file.RelativePath == sourceBefore.BlobRelativePath).Sha256;
        CreateWorldFixture(wgs, "Standard-2.json", "GSHIMPORTABC123", players:
        [new TestPlayer(0, "Stevenpwlk", true, 3, 4, "10,11,12")], seed: 123);

        var result = await adapter.ImportPortableArtifactAsync(
            artifact,
            baseline.BaselineDirectory!,
            "Stevenpwlk",
            "GSHIMPORTABC123",
            Path.Combine(_root, "pre-import"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("Standard-2.json", result.TargetLogicalName);
        var after = await adapter.InspectLocalStorageAsync();
        var protectedWorld = after.Worlds.Single(world => world.LogicalName == "Standard-1.json");
        var protectedHash = after.Files.Single(file => file.RelativePath == protectedWorld.BlobRelativePath).Sha256;
        Assert.Equal(sourceBeforeHash, protectedHash);
        var imported = after.Worlds.Single(world => world.LogicalName == "Standard-2.json");
        Assert.Equal("SourceWorld", imported.DisplayName);
        Assert.Equal(99, imported.WorldSeed);
        Assert.Equal(artifact.Manifest!.PayloadSha256, after.Files.Single(file => file.RelativePath == imported.BlobRelativePath).Sha256);
    }

    [Fact]
    public async Task ImportRefusesArtifactWhenExpectedPlayerIsAbsentOrNotPreparedAsIdZero()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "SourceWorld", players:
        [
            new TestPlayer(0, "Stevenpwlk", true, 3, 4, "1,2,3"),
            new TestPlayer(7, "BoB XiMe", false, 5, 6, "7,8,9")
        ]);
        var adapter = CreateAdapter();
        var rawArtifact = await adapter.ExportPortableArtifactAsync("SourceWorld", Path.Combine(_root, "artifacts"));
        var baseline = await adapter.CreateImportBaselineAsync(Path.Combine(_root, "baseline"));
        CreateWorldFixture(wgs, "Standard-2.json", "GSHIMPORTABC123");

        var missing = await adapter.ImportPortableArtifactAsync(
            rawArtifact,
            baseline.BaselineDirectory!,
            "UnknownPlayer",
            "GSHIMPORTABC123",
            Path.Combine(_root, "pre-import-missing"));
        var notPrepared = await adapter.ImportPortableArtifactAsync(
            rawArtifact,
            baseline.BaselineDirectory!,
            "BoB XiMe",
            "GSHIMPORTABC123",
            Path.Combine(_root, "pre-import-not-prepared"));

        Assert.False(missing.Success);
        Assert.Contains(missing.Errors, error => error.Contains("n'existe pas", StringComparison.Ordinal));
        Assert.False(notPrepared.Success);
        Assert.Contains(notPrepared.Errors, error => error.Contains("pas été préparé", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportRefusesWhenProtectedWorldChangedAfterBaseline()
    {
        var wgs = CreateWgs();
        var sourceBlob = CreateWorldFixture(wgs, "Standard-1.json", "SourceWorld");
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("SourceWorld", Path.Combine(_root, "artifacts"));
        var baseline = await adapter.CreateImportBaselineAsync(Path.Combine(_root, "baseline"));
        await File.AppendAllTextAsync(sourceBlob, " ");
        CreateWorldFixture(wgs, "Standard-2.json", "GSHIMPORTABC123");

        var result = await adapter.ImportPortableArtifactAsync(
            artifact,
            baseline.BaselineDirectory!,
            "Tester",
            "GSHIMPORTABC123",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("protégé", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportRefusesWhenMoreThanOneNewWorldExists()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "SourceWorld");
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactAsync("SourceWorld", Path.Combine(_root, "artifacts"));
        var baseline = await adapter.CreateImportBaselineAsync(Path.Combine(_root, "baseline"));
        CreateWorldFixture(wgs, "Standard-2.json", "GSHIMPORTABC123");
        CreateWorldFixture(wgs, "Standard-3.json", "OtherWorld");

        var result = await adapter.ImportPortableArtifactAsync(
            artifact,
            baseline.BaselineDirectory!,
            "Tester",
            "GSHIMPORTABC123",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("Un seul nouveau", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAcceptsInvisibleTrailingCharacterInPlaceholderDisplayName()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "SourceWorld");
        var adapter = CreateAdapter(activeNetworkRoute: true);
        var artifact = await adapter.ExportPortableArtifactAsync("SourceWorld", Path.Combine(_root, "artifacts"));
        var baseline = await adapter.CreateImportBaselineAsync(Path.Combine(_root, "baseline"));
        CreateWorldFixture(wgs, "Standard-2.json", "GSHIMPORTABC123\u200B");

        var result = await adapter.ImportPortableArtifactAsync(
            artifact,
            baseline.BaselineDirectory!,
            "Tester",
            "GSHIMPORTABC123",
            Path.Combine(_root, "pre-import"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public async Task ProbeImportTargetCapturesPlaceholderWithoutWriting()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "SourceWorld");
        var adapter = CreateAdapter(activeNetworkRoute: true);
        var baseline = await adapter.CreateImportBaselineAsync(Path.Combine(_root, "baseline"));
        CreateWorldFixture(wgs, "Standard-2.json", "GSHIMPORTABC123");

        var result = await adapter.ProbeImportTargetAsync(baseline.BaselineDirectory!, "GSHIMPORTABC123");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("Standard-2.json", result.TargetLogicalName);
        Assert.False(string.IsNullOrWhiteSpace(result.PlaceholderPayloadSha256));
        var after = await adapter.InspectLocalStorageAsync();
        Assert.Equal("GSHIMPORTABC123", after.Worlds.Single(world => world.LogicalName == "Standard-2.json").DisplayName);
    }

    [Fact]
    public async Task ReconcileImportDistinguishesPlaceholderFromImportedArtifact()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "SourceWorld");
        var adapter = CreateAdapter(activeNetworkRoute: true);
        var artifact = await adapter.ExportPortableArtifactAsync("SourceWorld", Path.Combine(_root, "artifacts"));
        var baseline = await adapter.CreateImportBaselineAsync(Path.Combine(_root, "baseline"));
        CreateWorldFixture(wgs, "Standard-2.json", "GSHIMPORTABC123");
        var probe = await adapter.ProbeImportTargetAsync(baseline.BaselineDirectory!, "GSHIMPORTABC123");

        var before = await adapter.ReconcilePortableImportAsync(
            artifact,
            baseline.BaselineDirectory!,
            "Tester",
            probe.TargetLogicalName!,
            probe.PlaceholderPayloadSha256!);
        var imported = await adapter.ImportPortableArtifactAsync(
            artifact,
            baseline.BaselineDirectory!,
            "Tester",
            "GSHIMPORTABC123",
            Path.Combine(_root, "pre-import"));
        var after = await adapter.ReconcilePortableImportAsync(
            artifact,
            baseline.BaselineDirectory!,
            "Tester",
            probe.TargetLogicalName!,
            probe.PlaceholderPayloadSha256!);

        Assert.Equal(ImportReconciliationState.PlaceholderIntact, before.State);
        Assert.True(imported.Success, string.Join(Environment.NewLine, imported.Errors));
        Assert.Equal(ImportReconciliationState.ImportedArtifactPresent, after.State);
    }

    [Fact]
    public async Task SaveStabilityIsReadOnlyAndReturnsStableWhenNothingChanges()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "SourceWorld");
        var adapter = CreateAdapter();

        var result = await adapter.WaitForSaveStabilityAsync(TimeSpan.FromMilliseconds(10));

        Assert.True(result.IsStable, string.Join(Environment.NewLine, result.ChangedFiles));
        Assert.Empty(result.ChangedFiles);
    }

    [Fact]
    public async Task SaveStabilityRefusesWhileGameIsRunning()
    {
        CreateWgs();
        var adapter = CreateAdapter(() => [(42, "Planet Crafter")]);

        var result = await adapter.WaitForSaveStabilityAsync(TimeSpan.FromMilliseconds(10));

        Assert.False(result.IsStable);
        Assert.Contains("game-running", result.ChangedFiles);
    }

    [Fact]
    public async Task LogicalComparisonIgnoresRotatedPhysicalBlobName()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1");
        var adapter = CreateAdapter();
        var before = await adapter.CreateSafetySnapshotAsync(Path.Combine(_root, "snapshots"), "Shlags1");
        CreateWorldFixture(wgs, "Standard-2.json", "Shlags1", rotatedPointer: true);
        var after = await adapter.CreateSafetySnapshotAsync(Path.Combine(_root, "snapshots"), "Shlags1");

        var difference = await adapter.CompareSnapshotsLogicallyAsync(before.SnapshotDirectory!, after.SnapshotDirectory!);

        var world = Assert.Single(difference.Files, file => file.LogicalName == "Standard-2.json");
        Assert.Equal("Unchanged", world.Status);
        Assert.Equal(world.BeforeSha256, world.AfterSha256);
    }

    private PlanetCrafterGamePassAdapter CreateAdapter(
        Func<IReadOnlyList<(int Id, string Name)>>? processProbe = null,
        bool activeNetworkRoute = false,
        Func<string, string>? finalPathResolver = null) =>
        new(new PlanetCrafterGamePassOptions
        {
            LocalApplicationDataOverride = _root,
            ProcessProbe = processProbe ?? (() => []),
            ActiveNetworkRouteProbe = () => activeNetworkRoute,
            FinalPathResolver = finalPathResolver
        });

    private string CreateWgs()
    {
        var path = Path.Combine(_root, "Packages", PlanetCrafterGamePassOptions.DefaultPackageFamilyName, "SystemAppData", "wgs");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateWorldFixture(
        string wgs,
        string logicalName,
        string displayName,
        bool rotatedPointer = false,
        IReadOnlyList<TestPlayer>? players = null,
        long seed = 42)
    {
        var safeName = new string(logicalName.Where(char.IsLetterOrDigit).ToArray());
        var container = Directory.CreateDirectory(Path.Combine(wgs, "C-" + safeName)).FullName;
        var blobId = Guid.NewGuid();
        var blobName = blobId.ToString("N").ToUpperInvariant();
        players ??= [new TestPlayer(0, "Tester", true, 3, 4, "1,2,3")];
        var playerRecords = string.Join("|\r\n", players.Select(player =>
            $"{{\"id\":{player.Id},\"name\":{System.Text.Json.JsonSerializer.Serialize(player.Name)},\"inventoryId\":{player.InventoryId},\"equipmentId\":{player.EquipmentId},\"host\":{player.IsHost.ToString().ToLowerInvariant()},\"planetId\":\"Prime\",\"playerPosition\":\"{player.Position}\"}}"));
        File.WriteAllText(
            Path.Combine(container, blobName),
            $"\r{{\"terraTokens\":0}}\r@\r{playerRecords}\r@\r{{\"saveDisplayName\":{System.Text.Json.JsonSerializer.Serialize(displayName)},\"planetId\":\"Prime\",\"mode\":\"Standard\",\"worldSeed\":{seed}}}\r@\r@");
        var metadata = new byte[168];
        BitConverter.GetBytes(4).CopyTo(metadata, 0);
        BitConverter.GetBytes(1).CopyTo(metadata, 4);
        System.Text.Encoding.Unicode.GetBytes(logicalName).CopyTo(metadata, 8);
        (rotatedPointer ? Guid.NewGuid() : blobId).TryWriteBytes(metadata.AsSpan(136, 16));
        blobId.TryWriteBytes(metadata.AsSpan(152, 16));
        File.WriteAllBytes(Path.Combine(container, "container.1"), metadata);
        return Path.Combine(container, blobName);
    }

    private sealed record TestPlayer(int Id, string Name, bool IsHost, int InventoryId, int EquipmentId, string Position);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
