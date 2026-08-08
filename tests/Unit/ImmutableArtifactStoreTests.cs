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
