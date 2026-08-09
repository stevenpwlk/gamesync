using GameSaveHub.Server.Infrastructure;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameSaveHub.UnitTests;

public sealed class ImmutableArtifactStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameSaveHubStoreTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ChunkWriteIsIdempotentForSameContent()
    {
        var store = CreateStore();
        var uploadId = Guid.NewGuid();
        var bytes = Enumerable.Range(0, 1024).Select(x => (byte)(x % 251)).ToArray();

        var first = await store.PutChunkAsync(uploadId, 0, new MemoryStream(bytes), bytes.Length, CancellationToken.None);
        var repeated = await store.PutChunkAsync(uploadId, 0, new MemoryStream(bytes), bytes.Length, CancellationToken.None);

        Assert.Equal(first, repeated);
        Assert.True(File.Exists(store.GetChunkPath(uploadId, 0)));
    }

    [Fact]
    public async Task CleanupRemovesPendingChunksOfAnUpload()
    {
        var store = CreateStore();
        var uploadId = Guid.NewGuid();
        await store.PutChunkAsync(uploadId, 0, new MemoryStream([1, 2, 3]), 3, CancellationToken.None);
        var chunk = store.GetChunkPath(uploadId, 0);
        Assert.True(File.Exists(chunk));

        store.TryCleanupPending(uploadId);

        Assert.False(File.Exists(chunk));
        Assert.False(Directory.Exists(Path.GetDirectoryName(chunk)!));
    }

    [Fact]
    public void CleanupOfAnUnknownUploadIsHarmless()
    {
        var store = CreateStore();

        // Un commit rejoué après nettoyage ne doit pas faire échouer la publication.
        store.TryCleanupPending(Guid.NewGuid());
    }

    [Fact]
    public async Task CleanupLeavesOtherUploadsIntact()
    {
        var store = CreateStore();
        var kept = Guid.NewGuid();
        var removed = Guid.NewGuid();
        await store.PutChunkAsync(kept, 0, new MemoryStream([1, 2, 3]), 3, CancellationToken.None);
        await store.PutChunkAsync(removed, 0, new MemoryStream([4, 5, 6]), 3, CancellationToken.None);

        store.TryCleanupPending(removed);

        Assert.True(File.Exists(store.GetChunkPath(kept, 0)));
        Assert.False(File.Exists(store.GetChunkPath(removed, 0)));
    }

    [Fact]
    public async Task ChunkWriteRejectsDifferentDuplicate()
    {
        var store = CreateStore();
        var uploadId = Guid.NewGuid();
        await store.PutChunkAsync(uploadId, 0, new MemoryStream([1, 2, 3]), 3, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PutChunkAsync(uploadId, 0, new MemoryStream([3, 2, 1]), 3, CancellationToken.None));
    }

    [Fact]
    public async Task ChunkWriteRejectsOversize()
    {
        var store = CreateStore(maxChunkBytes: 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PutChunkAsync(Guid.NewGuid(), 0, new MemoryStream([1, 2, 3]), 3, CancellationToken.None));
    }

    [Fact]
    public async Task LocalImportPublishesValidEnvelopeAndIsIdempotent()
    {
        var source = CreateArtifact();
        var store = CreateStore();

        var first = await store.ImportLocalAsync(source);
        var repeated = await store.ImportLocalAsync(source);

        Assert.Equal(first.Path, repeated.Path);
        Assert.Equal(first.Sha256, repeated.Sha256);
        Assert.Equal(first.Length, repeated.Length);
        Assert.Equal(first.Summary.PayloadSha256, repeated.Summary.PayloadSha256);
        Assert.Equal(new FileInfo(source).Length, first.Length);
        Assert.True(File.Exists(first.Path));
        Assert.Equal(first.Sha256 + ".gshsave", Path.GetFileName(first.Path));
    }

    [Fact]
    public async Task ConcurrentLocalImportsOfSameObjectAreIdempotent()
    {
        var source = CreateArtifact();
        var store = CreateStore();

        var imports = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.ImportLocalAsync(source)));

        Assert.Single(imports.Select(import => import.Path).Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.All(imports, import => Assert.True(File.Exists(import.Path)));
    }

    [Fact]
    public async Task StagedImportPublishesExactlyTheValidatedSnapshotWhenSourceChanges()
    {
        var source = CreateArtifact();
        var store = CreateStore();
        await using var staged = await store.StageLocalAsync(source);
        var stagedHash = staged.Sha256;
        await File.WriteAllTextAsync(source, "source remplacée après validation");

        var published = await store.PublishStagedAsync(staged);

        Assert.Equal(stagedHash, published.Sha256);
        Assert.Equal(stagedHash, await GameSaveHub.Core.FileSafety.ComputeSha256Async(published.Path));
    }

    [Fact]
    public async Task LocalImportRejectsInvalidEnvelopeWithoutPublishingObject()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "invalid.gshsave");
        await File.WriteAllTextAsync(source, "not a zip");
        var store = CreateStore();

        await Assert.ThrowsAnyAsync<Exception>(() => store.ImportLocalAsync(source));

        Assert.False(Directory.Exists(Path.Combine(_root, "objects")));
    }

    private ImmutableArtifactStore CreateStore(int maxChunkBytes = 4096) => new(Options.Create(new StorageOptions
    {
        Root = _root,
        MaxArtifactBytes = 1024 * 1024,
        MaxChunkBytes = maxChunkBytes
    }));

    private string CreateArtifact()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "source-" + Guid.NewGuid().ToString("N") + ".gshsave");
        var player = new { id = 0, name = "Bob", isHost = true, inventoryId = 1, equipmentId = 2 };
        var playerRecord = JsonSerializer.Serialize(new
        {
            player.id,
            player.name,
            host = player.isHost,
            player.inventoryId,
            player.equipmentId,
            planetId = "Prime",
            playerPosition = "1,2,3"
        });
        var metadata = JsonSerializer.Serialize(new
        {
            saveDisplayName = "Partie commune",
            planetId = "Prime",
            mode = "Standard",
            worldSeed = 42
        });
        var payload = System.Text.Encoding.UTF8.GetBytes(
            $"\r{{\"terraTokens\":0}}\r@\r{playerRecord}\r@\r{metadata}\r@\r@");
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
                displayName = "Partie commune",
                worldSeed = 42,
                players = new[] { player }
            }));
        }
        var payloadEntry = archive.CreateEntry("payload/world.save", CompressionLevel.Optimal);
        using (var stream = payloadEntry.Open()) stream.Write(payload);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
