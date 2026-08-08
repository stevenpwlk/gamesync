using System.Text.Json;

namespace GameSaveHub.Client.Orchestration;

public sealed class FileTransferSessionStore(string rootPath) : ITransferSessionStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions JournalJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string RootPath { get; } = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rootPath));

    public string GetSessionDirectory(Guid localSessionId) => Path.Combine(RootPath, localSessionId.ToString("D"));

    public async Task<TransferSession?> ReadAsync(Guid localSessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadCoreAsync(localSessionId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TransferSession>> ReadActiveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(RootPath)) return [];
            var sessions = new List<TransferSession>();
            foreach (var directory in Directory.EnumerateDirectories(RootPath).Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(Path.GetFileName(directory), out var id)) continue;
                var session = await ReadCoreAsync(id, cancellationToken);
                if (session is not null && TransferStageRules.HoldsLocalLock(session.Stage)) sessions.Add(session);
            }
            return sessions.OrderByDescending(session => session.UpdatedAtUtc).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(TransferSession session, string eventName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = GetSessionDirectory(session.LocalSessionId);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "session.json");
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);

            var journalPath = Path.Combine(directory, "events.ndjson");
            var journalEntry = JsonSerializer.Serialize(new
            {
                session.Revision,
                session.UpdatedAtUtc,
                Event = eventName,
                Stage = session.Stage.ToString(),
                session.LastErrorCode
            }, JournalJsonOptions) + Environment.NewLine;
            await using var journal = new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.WriteThrough);
            var bytes = System.Text.Encoding.UTF8.GetBytes(journalEntry);
            await journal.WriteAsync(bytes, cancellationToken);
            await journal.FlushAsync(cancellationToken);
            journal.Flush(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TransferSession?> ReadCoreAsync(Guid localSessionId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetSessionDirectory(localSessionId), "session.json");
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var session = await JsonSerializer.DeserializeAsync<TransferSession>(stream, JsonOptions, cancellationToken);
        if (session is not null && session.SchemaVersion != TransferSession.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Schéma de session locale non pris en charge : {session.SchemaVersion}.");
        }
        return session;
    }

    public void Dispose() => _gate.Dispose();
}
