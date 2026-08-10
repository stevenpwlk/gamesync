using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class ManagedSlotResolverTests
{
    private const string Adapter = "planet-crafter-pc-gamepass";
    private const string Package = "MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t";
    private const string Player = "Stevenpwlk";
    private const string DesiredName = "GSH-MONDE-PARTAGE";
    private const string LegacyName = "GSH-SHLAGS-RETURN";
    private static readonly DateTimeOffset CapturedAtUtc = new(2026, 8, 9, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, 0, 0, ManagedSlotStatus.Missing)]
    [InlineData(false, 1, 0, ManagedSlotStatus.UnboundCandidate)]
    [InlineData(false, 0, 1, ManagedSlotStatus.LegacyCandidate)]
    [InlineData(false, 2, 0, ManagedSlotStatus.Ambiguous)]
    public void ResolveUnboundInventory(bool hasBinding, int desiredCount, int legacyCount, ManagedSlotStatus expected)
    {
        var inspection = InspectionWith(desiredCount, legacyCount);

        var result = ManagedSlotResolver.Resolve(hasBinding ? Binding() : null, inspection, Package, Player);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void BoundLogicalNameWinsOverVisibleHomonym()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding(logicalName: "Standard-5.json"),
            Inspection(World("Standard-5.json", DesiredName), World("Standard-8.json", DesiredName)),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.Ready, result.Status);
        Assert.Equal("Standard-5.json", result.Candidate!.LogicalName);
    }

    [Fact]
    public void BoundLogicalNameMatchesWindowsCaseInsensitiveStorage()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding(logicalName: "standard-5.JSON"),
            Inspection(World("Standard-5.json", DesiredName)),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.Ready, result.Status);
        Assert.Equal("Standard-5.json", result.Candidate!.LogicalName);
    }

    [Fact]
    public void ResolveRejectsBindingForAnotherPackage()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding() with { PackageFamilyName = "Other.Package_123" },
            Inspection(World("Standard-5.json", DesiredName)),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.BindingMismatch, result.Status);
        Assert.Null(result.Candidate);
        Assert.Equal("binding_package_mismatch", result.SafetyStopCode);
    }

    [Fact]
    public void ResolveRejectsBindingForAnotherAdapter()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding() with { AdapterId = "another-adapter" },
            Inspection(World("Standard-5.json", DesiredName)),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.BindingMismatch, result.Status);
        Assert.Null(result.Candidate);
        Assert.Equal("binding_adapter_mismatch", result.SafetyStopCode);
    }

    [Fact]
    public void ResolveRejectsBindingForAnotherPlayer()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding() with { PlayerName = "Alex" },
            Inspection(World("Standard-5.json", DesiredName)),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.BindingMismatch, result.Status);
        Assert.Null(result.Candidate);
        Assert.Equal("binding_player_mismatch", result.SafetyStopCode);
    }

    [Fact]
    public void ResolveReportsMissingBoundLogicalName()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding(logicalName: "Standard-5.json"),
            Inspection(World("Standard-8.json", DesiredName)),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.BoundSlotMissing, result.Status);
        Assert.Null(result.Candidate);
        Assert.Equal("bound_slot_missing", result.SafetyStopCode);
    }

    [Fact]
    public void ResolveMarksBoundWorldWithUnexpectedDisplayNameForRename()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding(),
            Inspection(World("Standard-5.json", "A-RENOMMER")),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.RenamePending, result.Status);
        Assert.Equal("Standard-5.json", result.Candidate!.LogicalName);
        Assert.Null(result.SafetyStopCode);
    }

    [Fact]
    public void ResolveRejectsSelectedWorldWithoutTheRegisteredHost()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding(),
            Inspection(World("Standard-5.json", DesiredName, new DiscoveredPlayer(0, "Alex", true, null, null, 3, 4))),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.InvalidTopology, result.Status);
        Assert.Equal("Standard-5.json", result.Candidate!.LogicalName);
        Assert.Equal("invalid_host_topology", result.SafetyStopCode);
    }

    [Fact]
    public void ResolveAcceptsNormalizedRegisteredHostName()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding(),
            Inspection(World("Standard-5.json", DesiredName, new DiscoveredPlayer(0, "  stevenpwlk  ", true, null, null, 3, 4))),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.Ready, result.Status);
        Assert.Equal("Standard-5.json", result.Candidate!.LogicalName);
    }

    [Fact]
    public void ResolveRecognizesLegacyCandidate()
    {
        var result = ManagedSlotResolver.Resolve(
            null,
            Inspection(World("Standard-5.json", LegacyName)),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.LegacyCandidate, result.Status);
        Assert.Equal("Standard-5.json", result.Candidate!.LogicalName);
    }

    [Fact]
    public void ResolveRejectsUnboundInspectionForAnotherPackage()
    {
        var inspection = Inspection(World("Standard-5.json", DesiredName)) with { PackageFamilyName = "Other.Package_123" };

        var result = ManagedSlotResolver.Resolve(null, inspection, Package, Player);

        Assert.Equal(ManagedSlotStatus.BindingMismatch, result.Status);
        Assert.Null(result.Candidate);
        Assert.Equal("inspection_package_mismatch", result.SafetyStopCode);
    }

    [Fact]
    public void ResolveRejectsSelectedWorldWithAnotherHost()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding(),
            Inspection(World(
                "Standard-5.json",
                DesiredName,
                new DiscoveredPlayer(0, Player, true, null, null, 3, 4),
                new DiscoveredPlayer(7, "Alex", true, null, null, 5, 6))),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.InvalidTopology, result.Status);
        Assert.Equal("invalid_host_topology", result.SafetyStopCode);
    }

    [Fact]
    public void ResolveRejectsSelectedWorldWithAnotherPlayerIdZero()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding(),
            Inspection(World(
                "Standard-5.json",
                DesiredName,
                new DiscoveredPlayer(0, Player, true, null, null, 3, 4),
                new DiscoveredPlayer(0, "Alex", false, null, null, 5, 6))),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.InvalidTopology, result.Status);
        Assert.Equal("invalid_host_topology", result.SafetyStopCode);
    }

    [Fact]
    public void ResolveUsesThePermanentDesiredDisplayNameInsteadOfBindingMetadata()
    {
        var result = ManagedSlotResolver.Resolve(
            Binding() with { DesiredDisplayName = "AUTRE-NOM" },
            Inspection(World("Standard-5.json", "AUTRE-NOM")),
            Package,
            Player);

        Assert.Equal(ManagedSlotStatus.RenamePending, result.Status);
        Assert.Equal("Standard-5.json", result.Candidate!.LogicalName);
        Assert.Null(result.SafetyStopCode);
    }

    private static ManagedSlotBinding Binding(string logicalName = "Standard-5.json") =>
        ManagedSlotBinding.Create(Adapter, Package, Player, logicalName, LegacyName, DesiredName, CapturedAtUtc);

    private static LocalStorageInspection InspectionWith(int desiredCount, int legacyCount)
    {
        var worlds = Enumerable.Range(0, desiredCount)
            .Select(index => World($"Standard-{index + 1}.json", DesiredName))
            .Concat(Enumerable.Range(0, legacyCount)
                .Select(index => World($"Legacy-{index + 1}.json", LegacyName)))
            .ToArray();
        return Inspection(worlds);
    }

    private static LocalStorageInspection Inspection(params DiscoveredWorld[] worlds) =>
        new(1, Adapter, Package, CapturedAtUtc, false, true, [], [], worlds, []);

    private static DiscoveredWorld World(string logicalName, string displayName, params DiscoveredPlayer[] players) =>
        new(logicalName, displayName, null, null, null, $"blobs/{logicalName}", players.Length == 0
            ? [new DiscoveredPlayer(0, Player, true, null, null, 3, 4)]
            : players);
}
