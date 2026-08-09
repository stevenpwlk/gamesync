using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameSaveHub.Server.Infrastructure;

namespace GameSaveHub.UnitTests;

public sealed class ArtifactEnvelopeValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameSaveHubTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AcceptsValidEnvelope()
    {
        var players = new[]
        {
            new { id = 0, name = "Bob", isHost = true, inventoryId = 1, equipmentId = 2 }
        };
        var path = CreateArchive(
            "payload/world.save",
            CreateWorldPayload("Partie commune", 42, players),
            "Partie commune",
            42,
            players);
        await ArtifactEnvelopeValidator.ValidateAsync(path, 1024 * 1024);
    }


    [Fact]
    public async Task ReadsPlayerSummaryFromEnvelope()
    {
        var players = new[]
        {
            new { id = 0, name = "BoB XiMe", isHost = true, inventoryId = 5, equipmentId = 6 },
            new { id = 7, name = "Stevenpwlk", isHost = false, inventoryId = 3, equipmentId = 4 }
        };
        var path = CreateArchive(
            "payload/world.save",
            CreateWorldPayload("Shlags1", 569155654, players),
            "Shlags1",
            569155654,
            players);

        var summary = await ArtifactEnvelopeValidator.ReadSummaryAsync(path, 1024 * 1024);

        Assert.Equal("planet-crafter-pc-gamepass", summary.AdapterId);
        Assert.Equal("Shlags1", summary.DisplayName);
        Assert.Equal(569155654, summary.WorldSeed);
        Assert.Equal(2, summary.Players.Count);
        Assert.Contains(summary.Players, player => player.Id == 7 && player.Name == "Stevenpwlk" && player.InventoryId == 3 && player.EquipmentId == 4);
    }

    [Fact]
    public async Task RejectsTraversalEntry()
    {
        var path = CreateArchive("../world.save", RandomNumberGenerator.GetBytes(32));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ArtifactEnvelopeValidator.ValidateAsync(path, 1024 * 1024));
    }

    [Fact]
    public async Task RejectsHighlyCompressiblePayload()
    {
        var path = CreateArchive("payload/world.save", new byte[1024 * 1024]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ArtifactEnvelopeValidator.ValidateAsync(path, 2 * 1024 * 1024));
    }

    [Fact]
    public async Task RejectsPlayerManifestFabricatedOutsideWorldPayload()
    {
        var payloadPlayers = new[]
        {
            new { id = 0, name = "Bob", isHost = true, inventoryId = 1, equipmentId = 2 }
        };
        var fabricatedPlayers = new[]
        {
            new { id = 0, name = "Steven", isHost = true, inventoryId = 1, equipmentId = 2 }
        };
        var payload = CreateWorldPayload("Partie commune", 42, payloadPlayers);
        var path = CreateArchive(
            "payload/world.save",
            payload,
            "Partie commune",
            42,
            fabricatedPlayers);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArtifactEnvelopeValidator.ValidateAsync(path, 1024 * 1024));

        Assert.Contains("sémantique", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsDuplicatePayloadPlayerHiddenByManifest()
    {
        var payloadPlayers = new[]
        {
            new { id = 0, name = "Bob", isHost = true, inventoryId = 1, equipmentId = 2 },
            new { id = 0, name = "Steven", isHost = false, inventoryId = 3, equipmentId = 4 }
        };
        var manifestPlayers = payloadPlayers.Take(1).ToArray();
        var path = CreateArchive(
            "payload/world.save",
            CreateWorldPayload("Partie commune", 42, payloadPlayers),
            "Partie commune",
            42,
            manifestPlayers);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArtifactEnvelopeValidator.ValidateAsync(path, 1024 * 1024));
    }

    [Fact]
    public async Task RejectsMissingLogicalName()
    {
        var players = new[]
        {
            new { id = 0, name = "Bob", isHost = true, inventoryId = 1, equipmentId = 2 }
        };
        var path = CreateArchive(
            "payload/world.save",
            CreateWorldPayload("Partie commune", 42, players),
            "Partie commune",
            42,
            players,
            logicalName: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArtifactEnvelopeValidator.ValidateAsync(path, 1024 * 1024));
    }

    [Fact]
    public async Task RejectsInvalidUtf8EvenOutsideSemanticRecords()
    {
        var players = new[]
        {
            new { id = 0, name = "Bob", isHost = true, inventoryId = 1, equipmentId = 2 }
        };
        var valid = CreateWorldPayload("Partie commune", 42, players);
        var payload = valid.Concat(new byte[] { 0xC3, 0x28 }).ToArray();
        var path = CreateArchive("payload/world.save", payload, "Partie commune", 42, players);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            ArtifactEnvelopeValidator.ValidateAsync(path, 1024 * 1024));
    }

    private string CreateArchive(
        string payloadPath,
        byte[] payload,
        string? displayName = null,
        long? worldSeed = null,
        object? players = null,
        string? logicalName = "Standard-1.json")
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".gshsave");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(payload));
        var manifest = new
        {
            schemaVersion = 1,
            adapterId = "planet-crafter-pc-gamepass",
            logicalName,
            payloadPath,
            payloadLength = payload.LongLength,
            payloadSha256 = payloadHash,
            displayName,
            worldSeed,
            players
        };
        var manifestEntry = archive.CreateEntry("manifest.json");
        using (var writer = new StreamWriter(manifestEntry.Open())) writer.Write(JsonSerializer.Serialize(manifest));
        var payloadEntry = archive.CreateEntry(payloadPath, CompressionLevel.Optimal);
        using (var stream = payloadEntry.Open()) stream.Write(payload);
        return path;
    }

    private static byte[] CreateWorldPayload(string displayName, long seed, object players)
    {
        var playerJson = JsonSerializer.Serialize(players);
        using var playerDocument = JsonDocument.Parse(playerJson);
        var records = string.Join("|\r\n", playerDocument.RootElement.EnumerateArray().Select(player =>
        {
            var id = player.GetProperty("id").GetInt32();
            var name = player.GetProperty("name").GetString();
            var isHost = player.GetProperty("isHost").GetBoolean();
            var inventoryId = player.GetProperty("inventoryId").GetInt32();
            var equipmentId = player.GetProperty("equipmentId").GetInt32();
            return JsonSerializer.Serialize(new
            {
                id,
                name,
                inventoryId,
                equipmentId,
                host = isHost,
                planetId = "Prime",
                playerPosition = "1,2,3"
            });
        }));
        var metadata = JsonSerializer.Serialize(new
        {
            saveDisplayName = displayName,
            planetId = "Prime",
            mode = "Standard",
            worldSeed = seed
        });
        return System.Text.Encoding.UTF8.GetBytes($"\r{{\"terraTokens\":0}}\r@\r{records}\r@\r{metadata}\r@\r@");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
