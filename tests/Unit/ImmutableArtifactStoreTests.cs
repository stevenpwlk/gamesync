using GameSaveHub.Server.Infrastructure;
using Microsoft.Extensions.Options;

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

    private ImmutableArtifactStore CreateStore(int maxChunkBytes = 4096) => new(Options.Create(new StorageOptions
    {
        Root = _root,
        MaxArtifactBytes = 1024 * 1024,
        MaxChunkBytes = maxChunkBytes
    }));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
