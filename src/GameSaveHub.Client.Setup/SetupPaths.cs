namespace GameSaveHub.Client.Setup;

/// <summary>
/// Emplacements de référence du client installé, en un seul endroit pour les trois modes.
/// La distinction structurante : <see cref="InstallRoot"/> est renommé en bloc à chaque
/// mise à jour, <see cref="ProgramDataRoot"/> ne l'est jamais. Tout ce qui doit survivre
/// à une mise à jour (identité, état, slot permanent, configuration machine) vit donc
/// sous <see cref="ProgramDataRoot"/>.
/// </summary>
internal static class SetupPaths
{
    /// <summary>Version installée par cet exécutable. À garder alignée sur <c>$version</c> de <c>tools/build-lot3-setup.ps1</c>.</summary>
    public const string CurrentVersion = "0.5.0";

    public const string ServiceName = "GameSaveHubClient";

    public static string ProductRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameSaveHub");

    public static string InstallRoot => Path.Combine(ProductRoot, "Client");

    public static string OldInstallRoot => InstallRoot + ".old";

    public static string ProgramDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameSaveHub");

    /// <summary>Configuration par machine, hors du dossier basculé.</summary>
    public static string MachineConfigPath => Path.Combine(ProgramDataRoot, "appsettings.local.json");

    /// <summary>
    /// Emplacement historique écrit par <c>INSTALL-GAMESAVEHUB-CLIENT.ps1</c>. Lu uniquement
    /// pour reprendre la valeur du verrou d'écriture et pour migrer un poste déjà installé.
    /// </summary>
    public static string LegacyMachineConfigPath => Path.Combine(InstallRoot, "Service", "appsettings.local.json");

    public static string InstalledVersionPath => Path.Combine(InstallRoot, "VERSION");

    /// <summary>Copie stable de l'installateur, cible de la tâche planifiée.</summary>
    public static string InstalledSetupExePath => Path.Combine(ProductRoot, "GameSaveHub-Setup.exe");

    public static string ServiceExePath(string root) => Path.Combine(root, "Service", "GameSaveHub.Client.Service.exe");

    public static string AppExePath(string root) => Path.Combine(root, "App", "GameSaveHub.Client.App.exe");
}
