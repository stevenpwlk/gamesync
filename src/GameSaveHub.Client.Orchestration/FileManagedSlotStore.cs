using System.Text.Json;

namespace GameSaveHub.Client.Orchestration;

public sealed class FileManagedSlotStore(string path) : IManagedSlotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

    public async Task<ManagedSlotBinding?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var binding = await JsonSerializer.DeserializeAsync<ManagedSlotBinding>(stream, JsonOptions, cancellationToken);
        if (binding is not null && binding.SchemaVersion != ManagedSlotBinding.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Schéma de slot géré local non pris en charge : {binding.SchemaVersion}.");
        }

        return binding is { LastValidatedPlayers.Count: 0 }
            ? binding with { LastValidatedPlayers = Array.Empty<GameSaveHub.Contracts.DiscoveredPlayer>() }
            : binding;
    }

    public async Task WriteAsync(ManagedSlotBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");

        await using (var stream = new FileStream(
            temporary,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            65536,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, binding, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }

        File.Move(temporary, _path, overwrite: true);
    }
}
