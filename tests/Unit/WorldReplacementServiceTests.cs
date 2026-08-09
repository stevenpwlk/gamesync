using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameSaveHub.Core;
using GameSaveHub.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;

namespace GameSaveHub.UnitTests;

public sealed class WorldReplacementServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameSaveHubReplacementTests", Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReplacementProtectsPreviousVersionSwitchesPointerAndAuditsSource()
    {
        await using var fixture = await CreateFixtureAsync();
        var source = CreateArtifact(
            new Player(0, "BoB XiMe", true, 5, 6),
            new Player(7, "Stevenpwlk", false, 3, 4));

        var result = await fixture.Service.ReplaceAsync(new WorldReplacementRequest(
            fixture.WorldId,
            source,
            fixture.PreviousVersionId,
            "BoB XiMe",
            ["Stevenpwlk"],
            "Sauvegarde actuelle de Bob"));

        fixture.Db.ChangeTracker.Clear();
        var world = await fixture.Db.Worlds.SingleAsync();
        var previous = await fixture.Db.SaveVersions.SingleAsync(version => version.Id == fixture.PreviousVersionId);
        var replacement = await fixture.Db.SaveVersions.SingleAsync(version => version.Id == result.NewVersionId);
        var audit = await fixture.Db.AdminAudit.SingleAsync(entry => entry.Action == "world.replace");
        using var details = JsonDocument.Parse(audit.DetailsJson!);

        Assert.Equal(result.NewVersionId, world.CurrentVersionId);
        Assert.True(previous.IsProtected);
        Assert.Contains("Sauvegarde actuelle", previous.ProtectionReason, StringComparison.Ordinal);
        Assert.Equal("BoB XiMe", replacement.PublishedByPlayerName);
        Assert.Equal(fixture.PreviousVersionId, details.RootElement.GetProperty("previousVersionId").GetGuid());
        Assert.Equal(result.NewVersionId, details.RootElement.GetProperty("newVersionId").GetGuid());
        Assert.Equal("BoB XiMe", details.RootElement.GetProperty("sourcePlayer").GetString());
        Assert.Equal(_now, audit.PerformedAtUtc);
    }

    [Fact]
    public async Task ReplacementRefusesActiveSessionAndStaleExpectedVersion()
    {
        await using var locked = await CreateFixtureAsync(withActiveSession: true);
        var source = CreateArtifact(new Player(0, "Bob", true, 1, 2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => locked.Service.ReplaceAsync(new WorldReplacementRequest(
            locked.WorldId, source, locked.PreviousVersionId, "Bob", [], "Import bloqué par verrou")));
        Assert.False(File.Exists(locked.Store.GetObjectPath(await FileSafety.ComputeSha256Async(source))));
        locked.Db.ChangeTracker.Clear();
        Assert.Equal(locked.PreviousVersionId, (await locked.Db.Worlds.SingleAsync()).CurrentVersionId);
        Assert.Empty(await locked.Db.AdminAudit.ToListAsync());

        await using var stale = await CreateFixtureAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.Service.ReplaceAsync(new WorldReplacementRequest(
            stale.WorldId, source, Guid.NewGuid(), "Bob", [], "Version attendue périmée")));
        Assert.False(File.Exists(stale.Store.GetObjectPath(await FileSafety.ComputeSha256Async(source))));
        stale.Db.ChangeTracker.Clear();
        Assert.Equal(stale.PreviousVersionId, (await stale.Db.Worlds.SingleAsync()).CurrentVersionId);
    }

    [Fact]
    public async Task ReplacementRejectsMissingSourceAndInvalidTopologyBeforeMetadataChanges()
    {
        await using var fixture = await CreateFixtureAsync();
        var missing = CreateArtifact(new Player(0, "Steven", true, 1, 2));
        var invalid = CreateArtifact(
            new Player(0, "Bob", true, 1, 2),
            new Player(7, "Steven", false, 1, 4));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReplaceAsync(new WorldReplacementRequest(
            fixture.WorldId, missing, fixture.PreviousVersionId, "Bob", [], "Source absente du monde")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReplaceAsync(new WorldReplacementRequest(
            fixture.WorldId, invalid, fixture.PreviousVersionId, "Bob", [], "Topologie volontairement invalide")));

        Assert.False(File.Exists(fixture.Store.GetObjectPath(await FileSafety.ComputeSha256Async(missing))));
        Assert.False(File.Exists(fixture.Store.GetObjectPath(await FileSafety.ComputeSha256Async(invalid))));
        fixture.Db.ChangeTracker.Clear();
        Assert.Single(await fixture.Db.SaveVersions.ToListAsync());
        Assert.Empty(await fixture.Db.AdminAudit.ToListAsync());
    }

    [Fact]
    public async Task ReplacementReusesExistingHashAndPreservesEarlierProtection()
    {
        await using var fixture = await CreateFixtureAsync(previousProtection: "Conservation légale initiale");
        var source = CreateArtifact(new Player(0, "Bob", true, 1, 2));
        var imported = await fixture.Store.ImportLocalAsync(source);
        var reusableId = Guid.NewGuid();
        fixture.Db.SaveVersions.Add(new SaveVersionEntity
        {
            Id = reusableId,
            WorldId = fixture.WorldId,
            Sha256 = imported.Sha256,
            Length = imported.Length,
            StorageKey = imported.Sha256,
            CreatedAtUtc = _now.AddDays(-1),
            PublishedByPlayerName = "Bob"
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.ReplaceAsync(new WorldReplacementRequest(
            fixture.WorldId, source, fixture.PreviousVersionId, "Bob", [], "Réutilisation objet existant"));

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(reusableId, result.NewVersionId);
        Assert.True(result.ReusedVersion);
        Assert.Equal(2, await fixture.Db.SaveVersions.CountAsync());
        var previous = await fixture.Db.SaveVersions.SingleAsync(version => version.Id == fixture.PreviousVersionId);
        Assert.Equal("Conservation légale initiale", previous.ProtectionReason);
    }

    [Fact]
    public async Task RestoreReturnsToProtectedPreviousVersionAndWritesAudit()
    {
        await using var fixture = await CreateFixtureAsync();
        var replacementSource = CreateArtifact(new Player(0, "Bob", true, 10, 20));
        var replacement = await fixture.Service.ReplaceAsync(new WorldReplacementRequest(
            fixture.WorldId,
            replacementSource,
            fixture.PreviousVersionId,
            "Bob",
            [],
            "Nouvelle sauvegarde contrôlée"));

        await fixture.Service.RestoreAsync(new WorldRestoreRequest(
            fixture.WorldId,
            fixture.PreviousVersionId,
            "Rollback après validation pilote"));

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(fixture.PreviousVersionId, (await fixture.Db.Worlds.SingleAsync()).CurrentVersionId);
        var audit = await fixture.Db.AdminAudit.SingleAsync(entry => entry.Action == "world.restore");
        using var details = JsonDocument.Parse(audit.DetailsJson!);
        Assert.Equal(replacement.NewVersionId, details.RootElement.GetProperty("previousVersionId").GetGuid());
        Assert.Equal(fixture.PreviousVersionId, details.RootElement.GetProperty("restoredVersionId").GetGuid());
    }

    private async Task<Fixture> CreateFixtureAsync(bool withActiveSession = false, string? previousProtection = null)
    {
        Directory.CreateDirectory(_root);
        var databasePath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db");
        var options = new DbContextOptionsBuilder<GameSaveHubDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var db = new GameSaveHubDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new ImmutableArtifactStore(Options.Create(new StorageOptions
        {
            Root = Path.Combine(_root, "storage-" + Guid.NewGuid().ToString("N")),
            MaxArtifactBytes = 1024 * 1024,
            MaxChunkBytes = 4096
        }));
        var previousObject = await store.ImportLocalAsync(CreateArtifact(new Player(0, "Bob", true, 90, 91)));
        var worldId = Guid.NewGuid();
        var previousVersionId = Guid.NewGuid();
        db.Worlds.Add(new WorldEntity
        {
            Id = worldId,
            Name = "Monde principal",
            CurrentVersionId = previousVersionId,
            CreatedAtUtc = _now.AddMonths(-1)
        });
        db.SaveVersions.Add(new SaveVersionEntity
        {
            Id = previousVersionId,
            WorldId = worldId,
            Sha256 = previousObject.Sha256,
            Length = previousObject.Length,
            StorageKey = previousObject.Sha256,
            CreatedAtUtc = _now.AddMonths(-1),
            IsProtected = previousProtection is not null,
            ProtectionReason = previousProtection
        });
        if (withActiveSession)
        {
            db.Sessions.Add(new SessionEntity
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                DeviceId = Guid.NewGuid(),
                State = WorldSessionState.InGame,
                LastClientState = "InGame",
                CreatedAtUtc = _now.AddMinutes(-10),
                LastHeartbeatAtUtc = _now
            });
        }
        await db.SaveChangesAsync();

        var time = new FixedTimeProvider(_now);
        return new Fixture(db, store, new WorldReplacementService(db, store, time), worldId, previousVersionId);
    }

    private string CreateArtifact(params Player[] players)
    {
        var directory = Path.Combine(_root, "sources");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".gshsave");
        var playerRecords = string.Join("|\r\n", players.Select(player => JsonSerializer.Serialize(new
        {
            id = player.Id,
            name = player.Name,
            inventoryId = player.InventoryId,
            equipmentId = player.EquipmentId,
            host = player.IsHost,
            planetId = "Prime",
            playerPosition = "1,2,3"
        })));
        var metadata = JsonSerializer.Serialize(new
        {
            saveDisplayName = "Monde actuel",
            planetId = "Prime",
            mode = "Standard",
            worldSeed = 123456L
        });
        var payload = System.Text.Encoding.UTF8.GetBytes(
            $"\r{{\"terraTokens\":0}}\r@\r{playerRecords}\r@\r{metadata}\r@\r@");
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(payload));
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("manifest.json");
        using (var writer = new StreamWriter(manifestEntry.Open()))
        {
            writer.Write(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                adapterId = "planet-crafter-pc-gamepass",
                logicalName = "Standard-1.json",
                payloadPath = "payload/world.save",
                payloadLength = payload.LongLength,
                payloadSha256 = payloadHash,
                displayName = "Monde actuel",
                worldSeed = 123456L,
                players
            }));
        }
        var payloadEntry = archive.CreateEntry("payload/world.save", CompressionLevel.Optimal);
        using (var stream = payloadEntry.Open()) stream.Write(payload);
        return path;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed record Player(int Id, string Name, bool IsHost, int InventoryId, int EquipmentId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record Fixture(
        GameSaveHubDbContext Db,
        ImmutableArtifactStore Store,
        WorldReplacementService Service,
        Guid WorldId,
        Guid PreviousVersionId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
