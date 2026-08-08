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
        var path = CreateArchive("payload/world.save", RandomNumberGenerator.GetBytes(1024));
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
            RandomNumberGenerator.GetBytes(1024),
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

    private string CreateArchive(
        string payloadPath,
        byte[] payload,
        string? displayName = null,
        long? worldSeed = null,
        object? players = null)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".gshsave");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(payload));
        var manifest = new
        {
            schemaVersion = 1,
            adapterId = "planet-crafter-pc-gamepass",
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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
