using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Client.Service;

public sealed class ClientStateStore(IOptions<ClientServiceOptions> options) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Environment.ExpandEnvironmentVariables(options.Value.StatePath);

    public async Task<ClientPersistentState> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return Empty();
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<ClientPersistentState>(stream, JsonOptions, cancellationToken) ?? Empty();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(ClientPersistentState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Chemin d'état invalide.");
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
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

    private static ClientPersistentState Empty() => new(2, null, null, null, null, null, null, [], null);

    public void Dispose() => _gate.Dispose();
}
