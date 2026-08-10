namespace GameSaveHub.Client.Orchestration;

public enum FolderSwapReconciliationAction
{
    NoActionNeeded,
    CleanupOldFolder,
    RestoreFromOld,
    ManualReviewRequired
}

/// <summary>
/// Résout, sans I/O, l'état d'une bascule de dossier `Client`/`Client.old` après une
/// éventuelle coupure. Même style que <see cref="ManagedSlotResolver"/> : une fonction
/// pure prenant l'observation déjà faite par l'appelant, jamais de lecture disque ici.
/// </summary>
public static class FolderSwapReconciler
{
    public static FolderSwapReconciliationAction Resolve(bool clientExists, bool clientOldExists) => (clientExists, clientOldExists) switch
    {
        (true, false) => FolderSwapReconciliationAction.NoActionNeeded,
        (true, true) => FolderSwapReconciliationAction.CleanupOldFolder,
        (false, true) => FolderSwapReconciliationAction.RestoreFromOld,
        (false, false) => FolderSwapReconciliationAction.ManualReviewRequired
    };
}
