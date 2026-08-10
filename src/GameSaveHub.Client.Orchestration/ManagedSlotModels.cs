using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Orchestration;

public enum ManagedSlotStatus
{
    Missing,
    Ready,
    RenamePending,
    UnboundCandidate,
    LegacyCandidate,
    BoundSlotMissing,
    BindingMismatch,
    InvalidTopology,
    Ambiguous
}

public sealed record ManagedSlotResolution(
    ManagedSlotStatus Status,
    DiscoveredWorld? Candidate,
    string? SafetyStopCode);

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

    public bool Equals(ManagedSlotBinding? other) =>
        ReferenceEquals(this, other) || other is not null
        && SchemaVersion == other.SchemaVersion
        && AdapterId == other.AdapterId
        && PackageFamilyName == other.PackageFamilyName
        && PlayerName == other.PlayerName
        && LogicalName == other.LogicalName
        && CurrentDisplayName == other.CurrentDisplayName
        && DesiredDisplayName == other.DesiredDisplayName
        && BoundAtUtc == other.BoundAtUtc
        && LastValidatedAtUtc == other.LastValidatedAtUtc
        && LastValidatedPlayers.SequenceEqual(other.LastValidatedPlayers);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(AdapterId);
        hash.Add(PackageFamilyName);
        hash.Add(PlayerName);
        hash.Add(LogicalName);
        hash.Add(CurrentDisplayName);
        hash.Add(DesiredDisplayName);
        hash.Add(BoundAtUtc);
        hash.Add(LastValidatedAtUtc);
        foreach (var player in LastValidatedPlayers) hash.Add(player);
        return hash.ToHashCode();
    }
}
