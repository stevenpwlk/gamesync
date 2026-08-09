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
    public async Task ManagedSlotBaselineSelectsOnlyDeclaredLogicalNameAndProtectsEveryOtherWorld()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN", seed: 505);
        CreateWorldFixture(wgs, "Standard-2.json", "OtherWorld", seed: 202);
        CreateWorldFixture(wgs, "Standard-6.json", "GSH-SHLAGS-RETURN", seed: 606);
        var adapter = CreateAdapter();
        var output = Path.Combine(_root, "managed-baseline");

        var result = await adapter.CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            output);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.NotNull(result.Manifest);
        Assert.Equal("Standard-5.json", result.Manifest!.Target.LogicalName);
        Assert.Equal("GSH-SHLAGS-RETURN", result.Manifest.Target.CurrentDisplayName);
        Assert.Equal("GSH-MONDE-PARTAGE", result.Manifest.Target.DesiredDisplayName);
        Assert.Equal(505, result.Manifest.Target.WorldSeed);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))),
            result.Manifest.Target.BeforePayloadSha256);
        Assert.Equal(["Standard-2.json", "Standard-6.json"], result.Manifest.ProtectedWorlds.Select(world => world.LogicalName));
        Assert.DoesNotContain(result.Manifest.ProtectedWorlds, world => world.LogicalName == "Standard-5.json");
        Assert.Equal(6, result.Manifest.Files.Count);
        Assert.True(File.Exists(Path.Combine(result.BaselineDirectory!, "managed-slot-baseline.json")));
    }

    [Fact]
    public async Task ManagedSlotBaselineRejectsMissingDeclaredLogicalTarget()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-2.json", "GSH-SHLAGS-RETURN");

        var result = await CreateAdapter().CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            Path.Combine(_root, "managed-baseline"));

        Assert.False(result.Success);
        Assert.Null(result.BaselineDirectory);
        Assert.Contains(result.Errors, error => error.Contains("Standard-5.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManagedSlotBaselineRejectsCurrentDisplayNameMismatch()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "ActualDisplay");

        var result = await CreateAdapter().CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "DeclaredDisplay", "GSH-MONDE-PARTAGE"),
            Path.Combine(_root, "managed-baseline"));

        Assert.False(result.Success);
        Assert.Null(result.BaselineDirectory);
        Assert.Contains(result.Errors, error => error.Contains("affich", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ManagedSlotBaselineRejectsTargetWithMissingLocalHostPlayer()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN", players: []);

        var result = await CreateAdapter().CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            Path.Combine(_root, "managed-baseline"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("joueur", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ManagedSlotBaselineRejectsTargetWithAmbiguousLocalHostPlayer()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN", players:
        [
            new TestPlayer(0, "Alice", true, 3, 4, "1,2,3"),
            new TestPlayer(0, "Bob", true, 5, 6, "4,5,6")
        ]);

        var result = await CreateAdapter().CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            Path.Combine(_root, "managed-baseline"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ManagedSlotBaselineRefusesWhileGameIsRunning()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");

        var result = await CreateAdapter(() => [(42, "Planet Crafter")]).CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            Path.Combine(_root, "managed-baseline"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("Fermez", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManagedSlotBaselineRejectsWgsMutationAndPublishesNoBaselineDirectory()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 3) File.AppendAllText(targetBlob, " ");
            return [];
        });
        var output = Path.Combine(_root, "managed-baseline");

        var result = await adapter.CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            output);

        Assert.False(result.Success);
        Assert.Null(result.BaselineDirectory);
        Assert.Contains(result.Errors, error => error.Contains("chang", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.Exists(output) ? Directory.EnumerateFileSystemEntries(output) : []);
    }

    [Fact]
    public async Task ManagedSlotBaselineRejectsGameStartingDuringSecondObservationEvenWhenFilesAreEquivalent()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var output = Path.Combine(_root, "managed-baseline");
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            return probeCount == 4 ? [(42, "Planet Crafter")] : [];
        });

        var result = await adapter.CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            output);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("jeu", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task ManagedSlotBaselineRejectsUninterpretableWgsTopologyInsteadOfPublishingUnusableBaseline()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var malformedContainer = Directory.CreateDirectory(Path.Combine(wgs, "C-Broken"));
        var malformedMetadata = new byte[8];
        BitConverter.GetBytes(1).CopyTo(malformedMetadata, 4);
        await File.WriteAllBytesAsync(Path.Combine(malformedContainer.FullName, "container.1"), malformedMetadata);
        var output = Path.Combine(_root, "managed-baseline");

        var result = await CreateAdapter().CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            output);

        Assert.False(result.Success);
        Assert.Null(result.BaselineDirectory);
        Assert.Contains(result.Errors, error => error.Contains("interprét", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.Exists(output) ? Directory.EnumerateFileSystemEntries(output) : []);
    }

    [Fact]
    public async Task ManagedSlotBaselineRejectsOutputInsideOrContainingWgs()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");

        var inside = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(wgs, "baseline"));
        var containing = await adapter.CreateManagedSlotBaselineAsync(slot, Path.GetDirectoryName(wgs)!);

        Assert.False(inside.Success);
        Assert.False(containing.Success);
        Assert.Contains(inside.Errors, error => error.Contains("sépar", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(containing.Errors, error => error.Contains("sépar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ManagedSlotBaselineRejectsDefaultPhysicalLinkIntoWgsBeforeCopyingWhenSupported()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var linkedOutput = Path.Combine(_root, "linked-managed-baseline");
        if (!TryCreateDirectoryLink(linkedOutput, wgs)) return;
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await CreateAdapter().CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            linkedOutput);
        DeleteDirectoryLink(linkedOutput);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("sépar", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ManagedSlotBaselineReturnsFailureWhenPhysicalPathResolutionFails()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter(finalPathResolver: _ => throw new InvalidOperationException("physical-resolution-failed"));

        var result = await adapter.CreateManagedSlotBaselineAsync(
            new("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE"),
            Path.Combine(_root, "managed-baseline"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("physical-resolution-failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplaceManagedSlotKeepsLogicalNameAndCreatesNoWorld()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN", seed: 505);
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld", seed: 202);
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(
            adapter,
            "Standard-5.json",
            "Tester",
            "GSH-MONDE-PARTAGE",
            "prepared-nominal");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        Assert.True(baseline.Success, string.Join(Environment.NewLine, baseline.Errors));
        var beforeNames = (await adapter.InspectLocalStorageAsync()).Worlds.Select(world => world.LogicalName).ToArray();

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("Standard-5.json", result.TargetLogicalName);
        var after = await adapter.InspectLocalStorageAsync();
        Assert.Equal(beforeNames, after.Worlds.Select(world => world.LogicalName));
        var target = after.Worlds.Single(world => world.LogicalName == "Standard-5.json");
        Assert.Equal("GSH-MONDE-PARTAGE", target.DisplayName);
        Assert.Equal(artifact.Manifest!.PayloadSha256, after.Files.Single(file => file.RelativePath == target.BlobRelativePath).Sha256);
    }

    [Fact]
    public async Task ReplaceManagedSlotRejectsBackupPhysicalLinkIntoWgsBeforeSnapshotWhenSupported()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-linked-backup");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var linkedBackup = Path.Combine(_root, "linked-pre-import");
        if (!TryCreateDirectoryLink(linkedBackup, wgs)) return;
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            linkedBackup);
        DeleteDirectoryLink(linkedBackup);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("sépar", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReplaceManagedSlotRejectsTargetChangedSinceBaselineWithoutOverwritingIt()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-target-change");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        await File.AppendAllTextAsync(targetBlob, "external-change");
        var changedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("baseline", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(changedHash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotRejectsContainerWarningsBeforeWritingTargetBlob()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-warning-before-write");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var targetBefore = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));
        CreateMalformedContainerMetadata(wgs, "C-MALFORMED-BEFORE-WRITE");

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("conteneur", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(targetBefore, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotRejectsProtectedWorldChangedSinceBaseline()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var protectedBlob = CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-protected-change");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var targetBefore = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));
        await File.AppendAllTextAsync(protectedBlob, "external-change");

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("protég", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(targetBefore, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotRejectsPreparedArtifactWithWrongDesiredDisplayName()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "WRONG-DISPLAY", "prepared-wrong-display");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var targetBefore = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("affich", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(targetBefore, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotRejectsArtifactWithInvalidHostTopology()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "GSH-MONDE-PARTAGE", players:
        [
            new TestPlayer(0, "Tester", false, 3, 4, "1,2,3"),
            new TestPlayer(7, "Alice", true, 5, 6, "4,5,6")
        ]);
        var adapter = CreateAdapter();
        var artifact = await adapter.ExportPortableArtifactByLogicalNameAsync(
            "Standard-2.json",
            Path.Combine(_root, "invalid-topology-artifact"));
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var targetBefore = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Alice",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("topolog", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(targetBefore, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotRejectsUnexpectedLogicalWorldTopology()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-world-topology");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var targetBefore = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));
        CreateWorldFixture(wgs, "Standard-9.json", "UnexpectedWorld");

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("topologie", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(targetBefore, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotRejectsTargetMutationImmediatelyBeforeWrite()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-before-write");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 7) File.AppendAllText(targetBlob, "immediate-pre-write-change");
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("juste avant", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(artifact.Manifest!.PayloadSha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotBlocksPointerRotationAfterFinalHashBeforeWritingTargetBlob()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var targetDirectory = Path.GetDirectoryName(targetBlob)!;
        var containerMetadata = Path.Combine(targetDirectory, "container.1");
        var rotatedBlobId = Guid.NewGuid();
        var rotatedBlob = Path.Combine(targetDirectory, rotatedBlobId.ToString("N").ToUpperInvariant());
        File.Copy(targetBlob, rotatedBlob);
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-final-window");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var probeCount = 0;
        var rotationAttempted = false;
        var rotationBlocked = false;
        var writeHadAlreadyStarted = false;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 8)
            {
                rotationAttempted = true;
                writeHadAlreadyStarted = !Directory
                    .EnumerateFiles(targetDirectory, ".gsh-managed-import-*.tmp")
                    .Any();
                try
                {
                    RotateFixtureCurrentBlob(containerMetadata, rotatedBlobId);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    rotationBlocked = true;
                }
            }
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.True(rotationAttempted);
        Assert.False(writeHadAlreadyStarted);
        Assert.True(rotationBlocked);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(
            artifact.Manifest!.PayloadSha256,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotAtomicallyActivatesStagingWithoutRewritingOpenOldGeneration()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-atomic-activation");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var oldGenerationHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));
        var preparedPayloadHash = await ReadArtifactPayloadHashAsync(artifact.Path);
        await using var openOldGeneration = new FileStream(
            targetBlob,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        openOldGeneration.Position = 0;
        var stillOpenGenerationHash = Convert.ToHexStringLower(
            await System.Security.Cryptography.SHA256.HashDataAsync(openOldGeneration));
        var activatedPathHash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(oldGenerationHash, stillOpenGenerationHash);
        Assert.Equal(preparedPayloadHash, activatedPathHash);
        Assert.NotEqual(stillOpenGenerationHash, activatedPathHash);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(targetBlob)!, ".gsh-managed-import-*.tmp"));
    }

    [Fact]
    public async Task ReplaceManagedSlotCancellationAtFinalProbeLeavesOldGenerationIntactWithoutRollback()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-pre-activation-cancel");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var oldGenerationHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));
        var backupRoot = Path.Combine(_root, "pre-import");
        using var cancellation = new CancellationTokenSource();
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 8)
            {
                Directory.Delete(
                    Path.Combine(Directory.EnumerateDirectories(backupRoot).Single(), "wgs"),
                    recursive: true);
                cancellation.Cancel();
            }
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            backupRoot,
            cancellation.Token);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("annul", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Errors, error => error.Contains("rollback", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            oldGenerationHash,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotUsesStableGenerationWhenCurrentBlobIsMissing()
    {
        var wgs = CreateWgs();
        var stableBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        PointFixtureCurrentBlobToMissingGeneration(Path.Combine(Path.GetDirectoryName(stableBlob)!, "container.1"));
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-stable-fallback");
        var preparedPayloadHash = await ReadArtifactPayloadHashAsync(artifact.Path);
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(
            preparedPayloadHash,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(stableBlob))));
        var after = await adapter.InspectLocalStorageAsync();
        Assert.Equal(stableBlob, Path.Combine(wgs, after.Worlds.Single().BlobRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public async Task ReplaceManagedSlotSerializesConcurrentReplacersSoOnlyOneBaselinePayloadCanWin()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "SourceA", seed: 101);
        CreateWorldFixture(wgs, "Standard-2.json", "SourceB", seed: 202);
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN", seed: 505);
        var setupAdapter = CreateAdapter();
        var artifactA = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-1.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-concurrent-a");
        var artifactB = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-2.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-concurrent-b");
        var hashA = await ReadArtifactPayloadHashAsync(artifactA.Path);
        var hashB = await ReadArtifactPayloadHashAsync(artifactB.Path);
        Assert.NotEqual(hashA, hashB);
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        using var firstAtFinalProbe = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var firstProbeCount = 0;
        var firstAdapter = CreateAdapter(() =>
        {
            firstProbeCount++;
            if (firstProbeCount == 8)
            {
                firstAtFinalProbe.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
            }
            return [];
        });
        var secondAdapter = CreateAdapter();

        var firstTask = firstAdapter.ReplaceManagedSlotAsync(
            artifactA,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import-a"));
        Assert.True(firstAtFinalProbe.Wait(TimeSpan.FromSeconds(10)));
        PortableImportResult second;
        try
        {
            second = await secondAdapter.ReplaceManagedSlotAsync(
                artifactB,
                baseline.BaselineDirectory!,
                slot,
                "Tester",
                Path.Combine(_root, "pre-import-b"));
        }
        finally
        {
            releaseFirst.Set();
        }
        var first = await firstTask;

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Errors));
        Assert.False(second.Success);
        Assert.Equal(hashA, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
        Assert.NotEqual(hashB, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
    }

    [Fact]
    public async Task ReplaceManagedSlotRestoresGenerationThatWonAfterCasAndPreservesRejectedCandidate()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "SourceCandidate", seed: 101);
        CreateWorldFixture(wgs, "Standard-2.json", "SourceConcurrent", seed: 202);
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN", seed: 505);
        var setupAdapter = CreateAdapter();
        var candidateArtifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-1.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-stale-candidate");
        var concurrentArtifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-2.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-stale-winner");
        var candidateBytes = await ReadArtifactPayloadBytesAsync(candidateArtifact.Path);
        var concurrentBytes = await ReadArtifactPayloadBytesAsync(concurrentArtifact.Path);
        var candidateHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(candidateBytes));
        var concurrentHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(concurrentBytes));
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var backupRoot = Path.Combine(_root, "pre-import");
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 8)
            {
                var competingStaging = Path.Combine(Path.GetDirectoryName(targetBlob)!, $".competitor-{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(competingStaging, concurrentBytes);
                File.Replace(competingStaging, targetBlob, Path.Combine(_root, "competitor-evicted.blob"));
            }
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            candidateArtifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            backupRoot);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("stale", StringComparison.OrdinalIgnoreCase) || error.Contains("CAS", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(concurrentHash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
        var preservedCandidate = Assert.Single(Directory.EnumerateFiles(result.PreImportSnapshotDirectory!, "displaced-candidate.blob", SearchOption.AllDirectories));
        Assert.Equal(candidateHash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(preservedCandidate))));
    }

    [Fact]
    public async Task ReplaceManagedSlotRemovesExpectedEvictionBackupOnlyAfterSuccessfulValidation()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "Source", seed: 101);
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN", seed: 505);
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-1.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-backup-success");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Empty(Directory.EnumerateFiles(result.PreImportSnapshotDirectory!, "evicted-generation.blob", SearchOption.AllDirectories));
        Assert.Single(Directory.EnumerateFiles(result.PreImportSnapshotDirectory!, "completed.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ReplaceManagedSlotCleansExpectedEvictionBackupAfterRollbackPreservesCandidate()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "Source", seed: 101);
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN", seed: 505);
        var protectedBlob = CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld", seed: 202);
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-1.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-backup-rollback");
        var candidateHash = await ReadArtifactPayloadHashAsync(artifact.Path);
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 9) File.AppendAllText(protectedBlob, "force-post-activation-rollback");
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Empty(Directory.EnumerateFiles(result.PreImportSnapshotDirectory!, "evicted-generation.blob", SearchOption.AllDirectories));
        Assert.Single(Directory.EnumerateFiles(result.PreImportSnapshotDirectory!, "rolled-back.json", SearchOption.AllDirectories));
        var preservedCandidate = Assert.Single(Directory.EnumerateFiles(
            result.PreImportSnapshotDirectory!,
            "displaced-candidate.blob",
            SearchOption.AllDirectories));
        Assert.Equal(
            candidateHash,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(preservedCandidate))));
        Assert.True(File.Exists(preservedCandidate));
    }

    [Fact]
    public async Task ReplaceManagedSlotRecoversDurableInterruptedActivationBeforeEvaluatingNextReplacement()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-1.json", "InterruptedCandidate", seed: 101);
        CreateWorldFixture(wgs, "Standard-2.json", "NextCandidate", seed: 202);
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN", seed: 505);
        var adapter = CreateAdapter();
        var interruptedArtifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-1.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-interrupted");
        var nextArtifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-2.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-after-recovery");
        var interruptedBytes = await ReadArtifactPayloadBytesAsync(interruptedArtifact.Path);
        var interruptedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(interruptedBytes));
        var nextHash = await ReadArtifactPayloadHashAsync(nextArtifact.Path);
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var backupRoot = Path.Combine(_root, "pre-import");
        var interruptedSnapshot = await adapter.CreateSafetySnapshotAsync(backupRoot, "GSH-SHLAGS-RETURN");
        Assert.True(interruptedSnapshot.Success, string.Join(Environment.NewLine, interruptedSnapshot.Errors));
        var sessionDirectory = Directory.CreateDirectory(Path.Combine(interruptedSnapshot.SnapshotDirectory!, "managed-replacement-interrupted")).FullName;
        var preparedMarker = new
        {
            SchemaVersion = 1,
            TargetRelativePath = Path.GetRelativePath(wgs, targetBlob).Replace('\\', '/'),
            ExpectedBeforeSha256 = baseline.Manifest!.Target.BeforePayloadSha256,
            CandidateSha256 = interruptedHash,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            Path.Combine(sessionDirectory, "prepared.json"),
            System.Text.Json.JsonSerializer.Serialize(preparedMarker));
        var interruptedStaging = Path.Combine(Path.GetDirectoryName(targetBlob)!, $".interrupted-{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(interruptedStaging, interruptedBytes);
        File.Replace(interruptedStaging, targetBlob, Path.Combine(sessionDirectory, "evicted-generation.blob"));

        var result = await adapter.ReplaceManagedSlotAsync(
            nextArtifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            backupRoot);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(nextHash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob))));
        Assert.True(File.Exists(Path.Combine(sessionDirectory, "recovered.json")));
        var preservedInterrupted = Path.Combine(sessionDirectory, "displaced-candidate.blob");
        Assert.Equal(interruptedHash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(preservedInterrupted))));
        Assert.False(File.Exists(Path.Combine(sessionDirectory, "evicted-generation.blob")));
    }

    [Fact]
    public async Task ReplaceManagedSlotRollsBackFullSnapshotWhenProtectedWorldMutatesAfterWrite()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var protectedBlob = CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-after-write");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 9) File.AppendAllText(protectedBlob, "post-write-change");
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("protég", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("restaur", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReplaceManagedSlotRollsBackWithIndependentTokenWhenCancellationArrivesAfterWrite()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-cancel-rollback");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);
        using var cancellation = new CancellationTokenSource();
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 9) cancellation.Cancel();
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"),
            cancellation.Token);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("annul", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("rollback", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReplaceManagedSlotRollsBackWhenPostWriteInspectionThrowsInvalidOperation()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-invalid-operation-rollback");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 9) throw new InvalidOperationException("post-write-invalid-operation");
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("post-write-invalid-operation", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("rollback", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReplaceManagedSlotQuarantinesUnexpectedWgsFilesAndRestoresExactSnapshotSet()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var protectedBlob = CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-extra-quarantine");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);
        var backupRoot = Path.Combine(_root, "pre-import");
        var extraDirectory = Path.Combine(wgs, "C-UNEXPECTED");
        var extraWgsPath = Path.Combine(extraDirectory, "unexpected-after-write.bin");
        const string extraContent = "recoverable-unexpected-wgs-content";
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 9)
            {
                File.AppendAllText(protectedBlob, "post-write-protected-change");
                Directory.CreateDirectory(extraDirectory);
                File.WriteAllText(extraWgsPath, extraContent);
            }
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            backupRoot);

        Assert.False(result.Success);
        Assert.False(File.Exists(extraWgsPath));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
        var quarantinedExtra = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(result.PreImportSnapshotDirectory!, "rollback-quarantine"),
            Path.GetFileName(extraWgsPath),
            SearchOption.AllDirectories));
        Assert.Equal(extraContent, await File.ReadAllTextAsync(quarantinedExtra));
        Assert.Contains(result.Errors, error => error.Contains("quarantaine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReplaceManagedSlotRollsBackWhenFinalInspectionReportsContainerWarnings()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-warning-after-write");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);
        var warningPath = string.Empty;
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 9) warningPath = CreateMalformedContainerMetadata(wgs, "C-MALFORMED-AFTER-WRITE");
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("conteneur", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
        Assert.False(File.Exists(warningPath));
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(result.PreImportSnapshotDirectory!, "rollback-quarantine"),
            Path.GetFileName(warningPath),
            SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ReplaceManagedSlotReportsPrimaryAndRollbackErrorsWhenFullSnapshotCannotBeRead()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var setupAdapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(setupAdapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-rollback-failure");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await setupAdapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var backupRoot = Path.Combine(_root, "pre-import");
        var probeCount = 0;
        var adapter = CreateAdapter(() =>
        {
            probeCount++;
            if (probeCount == 9)
            {
                File.AppendAllText(targetBlob, "post-write-change");
                var snapshot = Directory.EnumerateDirectories(backupRoot).Single();
                Directory.Delete(Path.Combine(snapshot, "wgs"), recursive: true);
            }
            return [];
        });

        var result = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            backupRoot);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("après", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("ÉCHEC DU ROLLBACK", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplaceManagedSlotIsIdempotentForIdenticalArtifactAndNeverAddsLogicalWorld()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "prepared-idempotent");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var logicalNames = (await adapter.InspectLocalStorageAsync()).Worlds.Select(world => world.LogicalName).ToArray();
        var first = await adapter.ReplaceManagedSlotAsync(artifact, baseline.BaselineDirectory!, slot, "Tester", Path.Combine(_root, "pre-import-first"));

        var second = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import-second"));

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Errors));
        Assert.True(second.Success, string.Join(Environment.NewLine, second.Errors));
        var after = await adapter.InspectLocalStorageAsync();
        Assert.Equal(logicalNames, after.Worlds.Select(world => world.LogicalName));
        var target = after.Worlds.Single(world => world.LogicalName == "Standard-5.json");
        Assert.Equal(artifact.Manifest!.PayloadSha256, after.Files.Single(file => file.RelativePath == target.BlobRelativePath).Sha256);
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsPreviousPayloadPresentWithoutWritingWgs()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-previous");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");
        Assert.Equal(ManagedSlotReconciliationState.PreviousPayloadPresent, result.State);
        Assert.Equal(baseline.Manifest!.Target.BeforePayloadSha256, result.CurrentPayloadSha256);
        Assert.Equal(artifact.Manifest!.PayloadSha256, result.ExpectedImportedPayloadSha256);
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsUnexpectedContentForContainerWarningsWithoutWritingWgs()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-warning");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        CreateMalformedContainerMetadata(wgs, "C-MALFORMED-RECONCILE");
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");

        Assert.Equal(ManagedSlotReconciliationState.UnexpectedTargetContent, result.State);
        Assert.Contains(result.Errors, error => error.Contains("conteneur", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsImportedPayloadPresentWithoutWritingWgs()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-imported");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var replaced = await adapter.ReplaceManagedSlotAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester",
            Path.Combine(_root, "pre-import"));
        Assert.True(replaced.Success, string.Join(Environment.NewLine, replaced.Errors));
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");

        Assert.Equal(ManagedSlotReconciliationState.ImportedPayloadPresent, result.State);
        Assert.Equal(artifact.Manifest!.PayloadSha256, result.CurrentPayloadSha256);
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsTargetMissingWhenDeclaredLogicalWorldDisappears()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-missing");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        Directory.Delete(Path.GetDirectoryName(targetBlob)!, recursive: true);
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");

        Assert.Equal(ManagedSlotReconciliationState.TargetMissing, result.State);
        Assert.Null(result.CurrentPayloadSha256);
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsProtectedWorldChangedBeforeClassifyingTarget()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var protectedBlob = CreateWorldFixture(wgs, "Standard-2.json", "ProtectedWorld");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-protected");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        await File.AppendAllTextAsync(protectedBlob, "protected-change");
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");

        Assert.Equal(ManagedSlotReconciliationState.ProtectedWorldChanged, result.State);
        Assert.Contains(result.Errors, error => error.Contains("protég", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsUnexpectedTargetContentForThirdPayloadHash()
    {
        var wgs = CreateWgs();
        var targetBlob = CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-unexpected");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        await File.AppendAllTextAsync(targetBlob, "unexpected-third-payload");
        var unexpectedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(targetBlob)));
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");

        Assert.Equal(ManagedSlotReconciliationState.UnexpectedTargetContent, result.State);
        Assert.Equal(unexpectedHash, result.CurrentPayloadSha256);
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsInvalidBaselineForCorruptManifestWithoutWritingWgs()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-invalid-baseline");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        await File.WriteAllTextAsync(Path.Combine(baseline.BaselineDirectory!, "managed-slot-baseline.json"), "{}");
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");
        Assert.Equal(ManagedSlotReconciliationState.InvalidBaseline, result.State);
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsInvalidBaselineForNullFileEntryWithoutThrowingOrWritingWgs()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-null-file-entry");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var manifestPath = Path.Combine(baseline.BaselineDirectory!, "managed-slot-baseline.json");
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        var corruptJson = manifestJson.Replace("\"files\": [", "\"files\": [null,", StringComparison.Ordinal);
        Assert.NotEqual(manifestJson, corruptJson);
        await File.WriteAllTextAsync(manifestPath, corruptJson);
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");

        Assert.Equal(ManagedSlotReconciliationState.InvalidBaseline, result.State);
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsInvalidBaselineForReparsePointWithoutThrowingWhenSupported()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-reparse-baseline");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        var external = Directory.CreateDirectory(Path.Combine(_root, "reparse-target"));
        await File.WriteAllTextAsync(Path.Combine(external.FullName, "outside.txt"), "outside");
        var reparse = Path.Combine(baseline.BaselineDirectory!, "wgs", "linked-directory");
        if (!TryCreateDirectoryLink(reparse, external.FullName)) return;
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");

        DeleteDirectoryLink(reparse);
        Assert.Equal(ManagedSlotReconciliationState.InvalidBaseline, result.State);
        Assert.Contains(result.Errors, error => error.Contains("réanalyse", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
    }

    [Fact]
    public async Task ReconcileManagedSlotReportsInvalidArtifactForUnreadableArchiveWithoutWritingWgs()
    {
        var wgs = CreateWgs();
        CreateWorldFixture(wgs, "Standard-5.json", "GSH-SHLAGS-RETURN");
        var adapter = CreateAdapter();
        var artifact = await PrepareManagedSlotArtifactAsync(adapter, "Standard-5.json", "Tester", "GSH-MONDE-PARTAGE", "reconcile-invalid-artifact");
        var slot = new ManagedSlotReference("Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE");
        var baseline = await adapter.CreateManagedSlotBaselineAsync(slot, Path.Combine(_root, "managed-baseline"));
        await File.WriteAllTextAsync(artifact.Path, "not-a-portable-save-archive");
        var beforeFiles = await ReadFixtureFileHashesAsync(wgs);

        var result = await adapter.ReconcileManagedSlotReplacementAsync(
            artifact,
            baseline.BaselineDirectory!,
            slot,
            "Tester");

        Assert.Equal(ManagedSlotReconciliationState.InvalidArtifact, result.State);
        Assert.Equal(beforeFiles, await ReadFixtureFileHashesAsync(wgs));
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

    private static void RotateFixtureCurrentBlob(string containerMetadataPath, Guid blobId)
    {
        var metadata = File.ReadAllBytes(containerMetadataPath);
        blobId.TryWriteBytes(metadata.AsSpan(152, 16));
        File.WriteAllBytes(containerMetadataPath, metadata);
    }

    private static void PointFixtureCurrentBlobToMissingGeneration(string containerMetadataPath)
    {
        var metadata = File.ReadAllBytes(containerMetadataPath);
        Guid.NewGuid().TryWriteBytes(metadata.AsSpan(152, 16));
        File.WriteAllBytes(containerMetadataPath, metadata);
    }

    private static async Task<string> ReadArtifactPayloadHashAsync(string artifactPath)
    {
        using var archive = ZipFile.OpenRead(artifactPath);
        var payload = Assert.Single(archive.Entries, entry => entry.FullName == "payload/world.save");
        await using var stream = payload.Open();
        return Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(stream));
    }

    private static async Task<byte[]> ReadArtifactPayloadBytesAsync(string artifactPath)
    {
        using var archive = ZipFile.OpenRead(artifactPath);
        var payload = Assert.Single(archive.Entries, entry => entry.FullName == "payload/world.save");
        await using var stream = payload.Open();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private static string CreateMalformedContainerMetadata(string wgs, string directoryName)
    {
        var directory = Directory.CreateDirectory(Path.Combine(wgs, directoryName));
        var metadata = new byte[8];
        BitConverter.GetBytes(2).CopyTo(metadata, 4);
        var path = Path.Combine(directory.FullName, "container.1");
        File.WriteAllBytes(path, metadata);
        return path;
    }

    private async Task<PortableSaveArtifact> PrepareManagedSlotArtifactAsync(
        PlanetCrafterGamePassAdapter adapter,
        string logicalName,
        string playerName,
        string desiredDisplayName,
        string outputName)
    {
        var raw = await adapter.ExportPortableArtifactByLogicalNameAsync(
            logicalName,
            Path.Combine(_root, outputName, "raw"));
        var prepared = await adapter.PrepareForHostAsync(
            raw,
            playerName,
            desiredDisplayName,
            Path.Combine(_root, outputName, "prepared"));
        Assert.True(prepared.Success, string.Join(Environment.NewLine, prepared.Errors));
        return Assert.IsType<PortableSaveArtifact>(prepared.PreparedArtifact);
    }

    private static async Task<IReadOnlyList<(string RelativePath, string Sha256)>> ReadFixtureFileHashesAsync(string root)
    {
        var entries = new List<(string RelativePath, string Sha256)>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            entries.Add((
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path)))));
        }
        return entries;
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException or NotSupportedException)
        {
            if (!OperatingSystem.IsWindows()) return false;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(linkPath);
            startInfo.ArgumentList.Add(targetPath);
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null) return false;
            process.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void DeleteDirectoryLink(string linkPath)
    {
        if (Directory.Exists(linkPath)) Directory.Delete(linkPath);
    }

    private sealed record TestPlayer(int Id, string Name, bool IsHost, int InventoryId, int EquipmentId, string Position);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
