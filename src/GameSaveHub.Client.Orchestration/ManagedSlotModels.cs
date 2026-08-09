using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Orchestration;

public sealed record ManagedSlotBinding(
    int SchemaVersion,
    string AdapterId,
    string PackageFamilyName,
    string PlayerName,
    string LogicalName,
    string CurrentDisplayName,
    string DesiredDisplayName,
    DateTimeOffset BoundAtUtc,
    DateTimeOffset LastValidatedAtUtc,
    IReadOnlyList<DiscoveredPlayer> LastValidatedPlayers)
{
    public const int CurrentSchemaVersion = 1;

    public static ManagedSlotBinding Create(
        string adapterId,
        string packageFamilyName,
        string playerName,
        string logicalName,
        string observedDisplayName,
        string expectedDisplayName,
        DateTimeOffset boundAtUtc) => new(
            CurrentSchemaVersion,
            adapterId,
            packageFamilyName,
            playerName,
            logicalName,
            observedDisplayName,
            expectedDisplayName,
            boundAtUtc,
            boundAtUtc,
            Array.Empty<DiscoveredPlayer>());
}
