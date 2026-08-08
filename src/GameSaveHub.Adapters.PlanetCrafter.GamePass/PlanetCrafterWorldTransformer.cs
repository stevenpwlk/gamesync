using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameSaveHub.Contracts;

namespace GameSaveHub.Adapters.PlanetCrafter.GamePass;

internal static class PlanetCrafterWorldTransformer
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions CompactJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static HostTransformResult PrepareForHost(byte[] payload, IReadOnlyList<DiscoveredPlayer> manifestPlayers, string requestedPlayerName)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(manifestPlayers);
        if (string.IsNullOrWhiteSpace(requestedPlayerName))
        {
            return HostTransformResult.Failed(HostPreparationOutcome.PlayerNotFound, "Le pseudo du joueur cible est vide.");
        }

        var topologyError = ValidateTopology(manifestPlayers);
        if (topologyError is not null)
        {
            return HostTransformResult.Failed(HostPreparationOutcome.InvalidPlayerTopology, topologyError);
        }

        var normalizedRequested = NormalizePlayerName(requestedPlayerName);
        var matches = manifestPlayers
            .Where(player => NormalizePlayerName(player.Name).Equals(normalizedRequested, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            return HostTransformResult.Failed(
                HostPreparationOutcome.PlayerNotFound,
                $"Le joueur '{requestedPlayerName.Trim()}' n'existe pas dans cette sauvegarde. GameSave Hub refuse de réutiliser le personnage ID 0 d'un autre joueur.");
        }
        if (matches.Length > 1)
        {
            return HostTransformResult.Failed(
                HostPreparationOutcome.PlayerAmbiguous,
                $"Le pseudo '{requestedPlayerName.Trim()}' correspond à plusieurs joueurs. La préparation est refusée.");
        }

        var target = matches[0];
        var currentLocal = manifestPlayers.Single(player => player.Id == 0);
        var alreadyHost = target.Id == 0 && target.IsHost;

        string text;
        try
        {
            text = StrictUtf8.GetString(payload);
        }
        catch (DecoderFallbackException exception)
        {
            return HostTransformResult.Failed(HostPreparationOutcome.InvalidArtifact, $"Payload UTF-8 invalide : {exception.Message}");
        }

        var sections = text.Split("\r@\r", StringSplitOptions.None);
        var recordLocations = FindPlayerRecords(sections);
        if (recordLocations.Count != manifestPlayers.Count)
        {
            return HostTransformResult.Failed(
                HostPreparationOutcome.InvalidPlayerTopology,
                "Le nombre d'objets joueur dans le payload ne correspond pas au manifeste.");
        }

        var payloadPlayers = recordLocations.Select(location => location.Player).OrderBy(player => player.Id).ToArray();
        if (!PlayersEquivalentForTopology(payloadPlayers, manifestPlayers))
        {
            return HostTransformResult.Failed(
                HostPreparationOutcome.InvalidPlayerTopology,
                "Les joueurs sérialisés ne correspondent pas au manifeste validé.");
        }

        if (!alreadyHost)
        {
            foreach (var location in recordLocations)
            {
                var player = location.Player;
                var node = location.Node;
                if (player.Id == target.Id)
                {
                    node["id"] = 0;
                    node["host"] = true;
                }
                else if (player.Id == currentLocal.Id)
                {
                    node["id"] = target.Id;
                    node["host"] = false;
                }
                else
                {
                    node["host"] = false;
                }
                location.ReplaceSerialized(node.ToJsonString(CompactJson));
            }
        }

        var transformedText = string.Join("\r@\r", sections);
        var transformedPayload = StrictUtf8.GetBytes(transformedText);
        var expectedPlayers = manifestPlayers
            .Select(player => player.Id switch
            {
                var id when id == target.Id => player with { Id = 0, IsHost = true },
                0 => player with { Id = target.Id, IsHost = false },
                _ => player with { IsHost = false }
            })
            .OrderBy(player => player.Id)
            .ToArray();

        return new HostTransformResult(
            true,
            alreadyHost ? HostPreparationOutcome.AlreadyHost : HostPreparationOutcome.Prepared,
            transformedPayload,
            target.Name,
            target.Id,
            currentLocal.Id,
            !alreadyHost,
            expectedPlayers,
            []);

        List<PlayerRecordLocation> FindPlayerRecords(string[] mutableSections)
        {
            var locations = new List<PlayerRecordLocation>();
            for (var sectionIndex = 0; sectionIndex < mutableSections.Length; sectionIndex++)
            {
                var section = mutableSections[sectionIndex];
                var separator = section.Contains("|\r\n", StringComparison.Ordinal)
                    ? "|\r\n"
                    : section.Contains("|\n", StringComparison.Ordinal)
                        ? "|\n"
                        : null;
                var records = separator is null ? [section] : section.Split(separator, StringSplitOptions.None);
                var changed = false;
                for (var recordIndex = 0; recordIndex < records.Length; recordIndex++)
                {
                    if (!TryParsePlayerNode(records[recordIndex], out var node, out var player)) continue;
                    var capturedSection = sectionIndex;
                    var capturedRecord = recordIndex;
                    locations.Add(new PlayerRecordLocation(node!, player!, replacement =>
                    {
                        records[capturedRecord] = replacement;
                        mutableSections[capturedSection] = separator is null ? records[0] : string.Join(separator, records);
                    }));
                    changed = true;
                }
                if (changed)
                {
                    mutableSections[sectionIndex] = separator is null ? records[0] : string.Join(separator, records);
                }
            }
            return locations;
        }
    }

    private static string? ValidateTopology(IReadOnlyList<DiscoveredPlayer> players)
    {
        if (players.Count == 0) return "La sauvegarde ne contient aucun joueur.";
        if (players.Select(player => player.Id).Distinct().Count() != players.Count) return "Des IDs joueur sont dupliqués.";
        if (players.Select(player => player.InventoryId).Distinct().Count() != players.Count) return "Des inventaires joueur sont partagés ou dupliqués.";
        if (players.Select(player => player.EquipmentId).Distinct().Count() != players.Count) return "Des équipements joueur sont partagés ou dupliqués.";
        var local = players.Where(player => player.Id == 0).ToArray();
        if (local.Length != 1) return "La sauvegarde doit contenir exactement un joueur ID 0.";
        var hosts = players.Where(player => player.IsHost).ToArray();
        if (hosts.Length != 1) return "La sauvegarde doit contenir exactement un joueur hôte.";
        if (hosts[0].Id != 0) return "La sauvegarde est incohérente : le joueur hôte n'est pas le joueur ID 0.";
        return null;
    }

    private static bool TryParsePlayerNode(string record, out JsonObject? node, out DiscoveredPlayer? player)
    {
        node = null;
        player = null;
        try
        {
            node = JsonNode.Parse(record) as JsonObject;
            if (node is null ||
                !TryGetInt(node, "id", out var id) ||
                !TryGetString(node, "name", out var name) ||
                !TryGetBool(node, "host", out var host) ||
                !TryGetInt(node, "inventoryId", out var inventoryId) ||
                !TryGetInt(node, "equipmentId", out var equipmentId))
            {
                node = null;
                return false;
            }

            player = new DiscoveredPlayer(
                id,
                name ?? string.Empty,
                host,
                GetString(node, "planetId"),
                GetString(node, "playerPosition"),
                inventoryId,
                equipmentId);
            return true;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    private static bool PlayersEquivalentForTopology(DiscoveredPlayer[] left, IReadOnlyList<DiscoveredPlayer> right)
    {
        if (left.Length != right.Count) return false;
        var orderedLeft = left.OrderBy(player => player.Id).ToArray();
        var orderedRight = right.OrderBy(player => player.Id).ToArray();
        for (var index = 0; index < orderedLeft.Length; index++)
        {
            var a = orderedLeft[index];
            var b = orderedRight[index];
            if (a.Id != b.Id ||
                !a.Name.Equals(b.Name, StringComparison.Ordinal) ||
                a.IsHost != b.IsHost ||
                a.InventoryId != b.InventoryId ||
                a.EquipmentId != b.EquipmentId)
            {
                return false;
            }
        }
        return true;
    }

    internal static string NormalizePlayerName(string value) => value.Trim().Normalize(NormalizationForm.FormC);

    private static bool TryGetInt(JsonObject node, string name, out int value)
    {
        value = default;
        return node[name] is JsonValue json && json.TryGetValue(out value);
    }

    private static bool TryGetBool(JsonObject node, string name, out bool value)
    {
        value = default;
        return node[name] is JsonValue json && json.TryGetValue(out value);
    }

    private static bool TryGetString(JsonObject node, string name, out string? value)
    {
        value = null;
        return node[name] is JsonValue json && json.TryGetValue(out value);
    }

    private static string? GetString(JsonObject node, string name) =>
        node[name] is JsonValue json && json.TryGetValue<string>(out var value) ? value : null;

    internal sealed record HostTransformResult(
        bool Success,
        HostPreparationOutcome Outcome,
        byte[]? Payload,
        string? TargetPlayerName,
        int? TargetPlayerOriginalId,
        int? PreviousHostPlayerId,
        bool Changed,
        IReadOnlyList<DiscoveredPlayer>? PreparedPlayers,
        IReadOnlyList<string> Errors)
    {
        public static HostTransformResult Failed(HostPreparationOutcome outcome, string error) =>
            new(false, outcome, null, null, null, null, false, null, [error]);
    }

    private sealed record PlayerRecordLocation(JsonObject Node, DiscoveredPlayer Player, Action<string> ReplaceSerialized);
}
