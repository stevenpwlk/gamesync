using System.Text;
using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Orchestration;

public static class ManagedSlotResolver
{
    public const string PermanentDisplayName = "GSH-MONDE-PARTAGE";
    private const string LegacyDisplayName = "GSH-SHLAGS-RETURN";

    public static ManagedSlotResolution Resolve(
        ManagedSlotBinding? binding,
        LocalStorageInspection inspection,
        string packageFamilyName,
        string playerName)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFamilyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        if (!inspection.PackageFamilyName.Equals(packageFamilyName, StringComparison.Ordinal))
            return Stop(ManagedSlotStatus.BindingMismatch, "inspection_package_mismatch");

        if (binding is not null)
        {
            var identityFailure = ValidateBindingIdentity(binding, inspection, packageFamilyName, playerName);
            if (identityFailure is not null) return identityFailure;

            var boundWorlds = inspection.Worlds
                .Where(world => world.LogicalName.Equals(binding.LogicalName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (boundWorlds.Length == 0) return Stop(ManagedSlotStatus.BoundSlotMissing, "bound_slot_missing");
            if (boundWorlds.Length > 1) return Stop(ManagedSlotStatus.Ambiguous, "bound_slot_ambiguous");

            return ResolveCandidate(
                boundWorlds[0],
                playerName,
                boundWorlds[0].DisplayName.Equals(PermanentDisplayName, StringComparison.Ordinal)
                    ? ManagedSlotStatus.Ready
                    : ManagedSlotStatus.RenamePending);
        }

        var desiredWorlds = inspection.Worlds
            .Where(world => world.DisplayName.Equals(PermanentDisplayName, StringComparison.Ordinal))
            .ToArray();
        if (desiredWorlds.Length == 1)
            return ResolveCandidate(desiredWorlds[0], playerName, ManagedSlotStatus.UnboundCandidate);
        if (desiredWorlds.Length > 1) return Stop(ManagedSlotStatus.Ambiguous, "ambiguous_slot_candidates");

        var legacyWorlds = inspection.Worlds
            .Where(world => world.DisplayName.Equals(LegacyDisplayName, StringComparison.Ordinal))
            .ToArray();
        if (legacyWorlds.Length == 0) return Stop(ManagedSlotStatus.Missing, "managed_slot_missing");
        if (legacyWorlds.Length > 1) return Stop(ManagedSlotStatus.Ambiguous, "ambiguous_slot_candidates");

        return ResolveCandidate(legacyWorlds[0], playerName, ManagedSlotStatus.LegacyCandidate);
    }

    private static ManagedSlotResolution? ValidateBindingIdentity(
        ManagedSlotBinding binding,
        LocalStorageInspection inspection,
        string packageFamilyName,
        string playerName)
    {
        if (!binding.AdapterId.Equals(inspection.AdapterId, StringComparison.Ordinal))
            return Stop(ManagedSlotStatus.BindingMismatch, "binding_adapter_mismatch");
        if (!binding.PackageFamilyName.Equals(packageFamilyName, StringComparison.Ordinal))
            return Stop(ManagedSlotStatus.BindingMismatch, "binding_package_mismatch");
        if (!NormalizePlayerName(binding.PlayerName).Equals(NormalizePlayerName(playerName), StringComparison.OrdinalIgnoreCase))
            return Stop(ManagedSlotStatus.BindingMismatch, "binding_player_mismatch");
        return null;
    }

    private static ManagedSlotResolution ResolveCandidate(
        DiscoveredWorld candidate,
        string playerName,
        ManagedSlotStatus successStatus)
    {
        var playersWithIdZero = candidate.Players.Where(player => player.Id == 0).ToArray();
        var hosts = candidate.Players.Where(player => player.IsHost).ToArray();
        if (playersWithIdZero.Length != 1 || hosts.Length != 1 || !playersWithIdZero[0].IsHost ||
            !NormalizePlayerName(playersWithIdZero[0].Name).Equals(NormalizePlayerName(playerName), StringComparison.OrdinalIgnoreCase))
            return new ManagedSlotResolution(ManagedSlotStatus.InvalidTopology, candidate, "invalid_host_topology");

        return new ManagedSlotResolution(successStatus, candidate, null);
    }

    private static ManagedSlotResolution Stop(ManagedSlotStatus status, string code) => new(status, null, code);

    private static string NormalizePlayerName(string value) => value.Trim().Normalize(NormalizationForm.FormC);
}
