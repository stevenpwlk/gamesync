using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

#pragma warning disable CA1305

public sealed class ManagedSlotStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gsh-managed-slot-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteThenReadPreservesBindingAtomically()
    {
        var store = new FileManagedSlotStore(Path.Combine(_root, "managed-slot.json"));
        var binding = ManagedSlotBinding.Create(
            "planet-crafter-pc-gamepass", "MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t",
            "Stevenpwlk", "Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE",
            DateTimeOffset.Parse("2026-08-09T15:00:00Z"));

        await store.WriteAsync(binding);

        Assert.Equal(binding, await store.ReadAsync());
        Assert.Empty(Directory.GetFiles(_root, "*.tmp-*"));
    }

    [Fact]
    public async Task ReadRejectsUnsupportedSchemaWithoutRewritingFile()
    {
        var path = Path.Combine(_root, "managed-slot.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":99}");
        var before = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<InvalidDataException>(() => new FileManagedSlotStore(path).ReadAsync());

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

#pragma warning restore CA1305
