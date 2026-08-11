namespace GameSaveHub.Client.Orchestration;

/// <summary>
/// Ce que le compte système a le droit de demander au service par tube nommé.
/// La règle tient en une phrase : rien, sauf l'état de maintenance en lecture seule,
/// dont la tâche planifiée de mise à jour a besoin pour savoir si elle peut basculer les
/// dossiers. Fonction pure, du même style que <see cref="FolderSwapReconciler"/>, pour que
/// la règle soit testable sans monter un tube nommé ni un service Windows.
/// </summary>
public static class MaintenanceCommandPolicy
{
    public const string MaintenanceStatusCommand = "maintenance-status";

    public static bool IsAllowedForLocalSystem(string? command) =>
        string.Equals(command?.Trim(), MaintenanceStatusCommand, StringComparison.OrdinalIgnoreCase);
}
