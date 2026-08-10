using System.Text.Json;

namespace GameSaveHub.Client.Orchestration;

public sealed class FileManagedSlotStore(string path) : IManagedSlotStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ManagedSlotBinding?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return null;

            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var binding = await JsonSerializer.DeserializeAsync<ManagedSlotBinding>(stream, JsonOptions, cancellationToken);
            if (binding is not null && binding.SchemaVersion != ManagedSlotBinding.CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Schéma de slot géré local non pris en charge : {binding.SchemaVersion}.");
            }

            return binding is null
                ? null
                : binding with { LastValidatedPlayers = binding.LastValidatedPlayers.ToArray() };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(ManagedSlotBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        await _gate.WaitAsync(cancellationToken);
        try
        {
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
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
