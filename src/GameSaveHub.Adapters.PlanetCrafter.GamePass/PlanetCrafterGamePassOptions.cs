namespace GameSaveHub.Adapters.PlanetCrafter.GamePass;

public sealed record PlanetCrafterGamePassOptions
{
    public const string DefaultPackageFamilyName = "MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t";
    public string PackageFamilyName { get; init; } = DefaultPackageFamilyName;
    public string? LocalApplicationDataOverride { get; init; }
    public Func<IReadOnlyList<(int Id, string Name)>>? ProcessProbe { get; init; }
    public Func<bool>? ActiveNetworkRouteProbe { get; init; }
    public Func<string, string>? FinalPathResolver { get; init; }
}
