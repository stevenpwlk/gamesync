using GameSaveHub.Client.Setup;

var mode = args.Length > 0 ? args[0] : "--install";
return mode switch
{
    "--install" or "-install" => await RunInstallAsync(),
    "--auto-update" => await RunAutoUpdateAsync(),
    "--uninstall" => await RunUninstallAsync(),
    _ => Fail($"Mode inconnu : {mode}")
};

static async Task<int> RunInstallAsync()
{
    try
    {
        return await Installer.RunAsync("https://saves.stevenpwlk.fr:18443/", CancellationToken.None);
    }
    catch (Exception ex)
    {
        return Fail($"Échec de l'installation : {ex.Message}");
    }
}

// Ce mode tourne sans personne devant l'écran, depuis la tâche planifiée : une exception
// non rattrapée y produirait une trace .NET brute dans l'historique du planificateur, au
// lieu d'un message exploitable et d'un code de sortie propre.
static async Task<int> RunAutoUpdateAsync()
{
    try
    {
        return await Updater.RunAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        return Fail($"Échec de la mise à jour automatique : {ex.Message}");
    }
}

static async Task<int> RunUninstallAsync()
{
    try
    {
        return await Uninstaller.RunAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        return Fail($"Échec de la désinstallation : {ex.Message}");
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 3;
}
