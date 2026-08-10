namespace GameSaveHub.Client.Orchestration;

public interface IManagedSlotStore
{
    Task<ManagedSlotBinding?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(ManagedSlotBinding binding, CancellationToken cancellationToken = default);
}
