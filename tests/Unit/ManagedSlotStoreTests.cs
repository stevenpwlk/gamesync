using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

#pragma warning disable CA1305

public sealed class ManagedSlotStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gsh-managed-slot-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteThenReadPreservesBindingAtomically()
    {
        using var store = new FileManagedSlotStore(Path.Combine(_root, "managed-slot.json"));
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

        using var store = new FileManagedSlotStore(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync());

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task WriteThenReadPreservesNonEmptyValidatedPlayers()
    {
        using var store = new FileManagedSlotStore(Path.Combine(_root, "managed-slot.json"));
        var binding = ManagedSlotBinding.Create(
            "planet-crafter-pc-gamepass", "MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t",
            "Stevenpwlk", "Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE",
            DateTimeOffset.Parse("2026-08-09T15:00:00Z")) with
        {
            LastValidatedPlayers =
            [
                new DiscoveredPlayer(7, "Stevenpwlk", true, "Prime", "0,0,0", 3, 4),
                new DiscoveredPlayer(8, "Alex", false, "Prime", "1,2,3", 5, 6)
            ]
        };

        await store.WriteAsync(binding);

        Assert.Equal(binding, await store.ReadAsync());
    }

    [Fact]
    public async Task WriteSucceedsWhileReadIsOpen()
    {
        var path = Path.Combine(_root, "managed-slot.json");
        using var store = new FileManagedSlotStore(path);
        var initial = ManagedSlotBinding.Create(
            "planet-crafter-pc-gamepass", "MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t",
            new string('S', 16 * 1024 * 1024), "Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE",
            DateTimeOffset.Parse("2026-08-09T15:00:00Z"));
        var replacement = ManagedSlotBinding.Create(
            "planet-crafter-pc-gamepass", "MijuGames.ThePlanetCrafter_ta6nvwnbx9v7t",
            "Stevenpwlk", "Standard-5.json", "GSH-SHLAGS-RETURN", "GSH-MONDE-PARTAGE",
            DateTimeOffset.Parse("2026-08-09T15:01:00Z"));
        await store.WriteAsync(initial);

        var reading = store.ReadAsync();
        await WaitForOpenReadAsync(path, reading);

        await store.WriteAsync(replacement);

        await reading;
        Assert.Equal(replacement, await store.ReadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static async Task WaitForOpenReadAsync(string path, Task<ManagedSlotBinding?> reading)
    {
        for (var attempt = 0; attempt < 500 && !reading.IsCompleted; attempt++)
        {
            try
            {
                await using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return;
            }

            await Task.Delay(1);
        }

        Assert.Fail("La lecture du store n'a pas conservé le fichier ouvert.");
    }
}

#pragma warning restore CA1305
