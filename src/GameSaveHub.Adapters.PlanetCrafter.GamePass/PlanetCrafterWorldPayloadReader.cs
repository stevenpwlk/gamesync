using System.Text.Json;
using GameSaveHub.Contracts;

namespace GameSaveHub.Adapters.PlanetCrafter.GamePass;

public static class PlanetCrafterWorldPayloadReader
{
    public static DiscoveredWorld? Parse(string logicalName, string text, string blobRelativePath)
    {
        var sections = text.Split("\r@\r", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var players = new List<DiscoveredPlayer>();
        string? displayName = null;
        string? planetId = null;
        string? mode = null;
        long? worldSeed = null;
        foreach (var section in sections)
        {
            foreach (var record in section.Split(["|\r\n", "|\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                TryReadPlayer(record, players);
            }

            try
            {
                using var document = JsonDocument.Parse(section);
                var root = document.RootElement;
                if (!root.TryGetProperty("saveDisplayName", out var displayProperty)) continue;
                displayName = displayProperty.GetString() ?? logicalName;
                planetId = ReadString(root, "planetId");
                mode = ReadString(root, "mode");
                worldSeed = root.TryGetProperty("worldSeed", out var seed) && seed.TryGetInt64(out var value) ? value : null;
            }
            catch (JsonException)
            {
                // Some save sections contain pipe-separated JSON records; only the metadata section is needed here.
            }
        }

        return displayName is null
            ? null
            : new DiscoveredWorld(
                logicalName,
                displayName,
                planetId,
                mode,
                worldSeed,
                blobRelativePath,
                players.OrderBy(player => player.Id).ToArray());
    }

    private static void TryReadPlayer(string record, List<DiscoveredPlayer> players)
    {
        try
        {
            using var document = JsonDocument.Parse(record);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id) ||
                !root.TryGetProperty("name", out var name) ||
                !root.TryGetProperty("host", out var host) ||
                !root.TryGetProperty("inventoryId", out var inventoryId) ||
                !root.TryGetProperty("equipmentId", out var equipmentId)) return;
            players.Add(new DiscoveredPlayer(
                id.GetInt32(),
                name.GetString() ?? string.Empty,
                host.GetBoolean(),
                ReadString(root, "planetId"),
                ReadString(root, "playerPosition"),
                inventoryId.GetInt32(),
                equipmentId.GetInt32()));
        }
        catch (JsonException)
        {
            // Not a player record.
        }
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
