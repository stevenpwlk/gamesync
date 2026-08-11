using GameSaveHub.Client.Setup;

var mode = args.Length > 0 ? args[0] : "--install";
return mode switch
{
    "--install" or "-install" => await RunInstallAsync(),
    "--auto-update" => await Updater.RunAsync(CancellationToken.None),
    "--uninstall" => Fail("Le mode --uninstall sera disponible après la tâche 11 de ce plan."),
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

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 3;
}
