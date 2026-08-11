namespace GameSaveHub.Client.Orchestration;

/// <summary>
/// Décisions pures de la mise à jour silencieuse, extraites de <c>Updater.cs</c> pour être
/// testables sans service, sans réseau et sans disque — même style que
/// <see cref="FolderSwapReconciler"/> : l'appelant a déjà fait l'observation, on ne décide
/// qu'à partir d'elle.
/// </summary>
public static class UpdateDecision
{
    /// <summary>
    /// Une mise à jour ne s'applique que si la version publiée est <em>strictement</em>
    /// supérieure à celle installée. L'égalité ne suffisait pas comme seul garde-fou :
    /// elle laissait passer les retours en arrière, c'est-à-dire la réinstallation
    /// silencieuse d'une version plus ancienne — donc d'anciens bogues — sur le PC d'un
    /// joueur, sans qu'il l'ait demandé.
    /// </summary>
    /// <param name="installedVersion">
    /// Contenu du fichier <c>VERSION</c> installé. <c>null</c>, vide ou illisible signifie
    /// « état inconnu » : on applique, car refuser laisserait un poste dont le fichier a été
    /// corrompu bloqué pour toujours sur une version qu'on ne sait même pas nommer.
    /// </param>
    /// <param name="manifestVersion">
    /// Version annoncée par le manifeste signé. Illisible (format non <c>X.Y[.Z[.R]]</c>)
    /// signifie refus : on ne peut pas prouver qu'elle est plus récente, et une version qu'on
    /// ne sait pas comparer ne doit jamais déclencher une bascule de dossier.
    /// </param>
    public static bool ShouldApplyUpdate(string? installedVersion, string manifestVersion)
    {
        if (!TryParseVersion(manifestVersion, out var manifest)) return false;
        if (!TryParseVersion(installedVersion, out var installed)) return true;
        return manifest > installed;
    }

    /// <summary>
    /// Report de la mise à jour : un statut absent (service muet, tube injoignable) compte
    /// comme « non sûr » au même titre qu'un statut explicitement non sûr. Ne jamais basculer
    /// les dossiers quand on ignore si le jeu est ouvert.
    /// </summary>
    public static bool ShouldDeferUpdate(MaintenanceSafetyStatus? status) => status is null || !status.SafeToUpdate;

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!Version.TryParse(value.Trim(), out var parsed)) return false;
        version = parsed;
        return true;
    }
}
