namespace GameSaveHub.Client.Orchestration;

public interface ITransferSessionStore
{
    string RootPath { get; }

    /// <summary>
    /// Vrai tant qu'une écriture durable de session est en cours. Sert uniquement au
    /// contrôle de santé de l'updater (<c>maintenance-status</c>) ; ne doit jamais être
    /// utilisé pour sérialiser des écritures, ce rôle reste celui de
    /// <see cref="TransferTransitionGate"/>.
    /// </summary>
    bool IsWriteInProgress { get; }

    string GetSessionDirectory(Guid localSessionId);
    Task<TransferSession?> ReadAsync(Guid localSessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransferSession>> ReadActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Toutes les sessions, y compris terminées, abandonnées ou en échec.
    /// Nécessaire au rapport de diagnostic : c'est justement une session terminée
    /// en échec qu'il faut pouvoir relire après coup.
    /// </summary>
    Task<IReadOnlyList<TransferSession>> ReadAllAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(TransferSession session, string eventName, CancellationToken cancellationToken = default);
}
