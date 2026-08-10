using GameSaveHub.Core;
using GameSaveHub.Server.Infrastructure;
using Microsoft.Extensions.Options;

namespace GameSaveHub.UnitTests;

public sealed class ClientReleaseObjectStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gsh-release-store-" + Guid.NewGuid().ToString("N"));

    private ClientReleaseObjectStore CreateStore() =>
        new(Options.Create(new StorageOptions { Root = _root, MaxArtifactBytes = 64 * 1024 * 1024 }));

    [Fact]
    public async Task PutAsyncStoresFileUnderContentAddressedPath()
    {
        var source = Path.Combine(Path.GetTempPath(), "release-" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(source, "fake-zip-contents"u8.ToArray());
        var sha256 = await FileSafety.ComputeSha256Async(source);
        var store = CreateStore();

        var destination = await store.PutAsync(source, sha256, "0.5.0");

        Assert.True(File.Exists(destination));
        Assert.Equal(store.GetObjectPath(sha256, "0.5.0"), destination);
        Assert.Equal("fake-zip-contents"u8.ToArray(), await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task PutAsyncRejectsHashMismatch()
    {
        var source = Path.Combine(Path.GetTempPath(), "release-" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(source, "fake-zip-contents"u8.ToArray());
        var store = CreateStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PutAsync(source, new string('0', 64), "0.5.0"));
    }

    [Fact]
    public async Task PutAsyncIsIdempotentForIdenticalContent()
    {
        var source = Path.Combine(Path.GetTempPath(), "release-" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(source, "fake-zip-contents"u8.ToArray());
        var sha256 = await FileSafety.ComputeSha256Async(source);
        var store = CreateStore();

        await store.PutAsync(source, sha256, "0.5.0");
        var second = Path.Combine(Path.GetTempPath(), "release-" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(second, "fake-zip-contents"u8.ToArray());
        var destination = await store.PutAsync(second, sha256, "0.5.0");

        Assert.True(File.Exists(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
