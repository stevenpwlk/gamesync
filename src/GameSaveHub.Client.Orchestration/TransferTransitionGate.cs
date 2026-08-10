namespace GameSaveHub.Client.Orchestration;

public sealed class TransferTransitionGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _busy;

    /// <summary>
    /// Lecture seule, sans verrou : utilisée par le contrôle de santé de l'updater,
    /// jamais pour décider d'entrer soi-même dans une transition.
    /// </summary>
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public async Task<T> RunAsync<T>(Func<Task<T>> transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await _gate.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _busy, 1);
        try
        {
            return await transition();
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
