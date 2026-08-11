using System.Diagnostics;

namespace GameSaveHub.Client.Setup;

/// <summary>
/// Enregistre la tâche planifiée qui relance ce même exécutable en mode silencieux.
/// Passe par <c>schtasks.exe</c> plutôt que par l'API TaskScheduler COM : une seule
/// commande, pas de dépendance native supplémentaire dans un exécutable single-file.
/// </summary>
public static class ScheduledTaskManager
{
    private const string TaskName = "GameSaveHubUpdater";

    public static void Register(string exePath)
    {
        RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --auto-update\" /SC HOURLY /MO 6 /RL HIGHEST /RU SYSTEM /F");
    }

    public static void Remove()
    {
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F", ignoreMissing: true);
    }

    private static void RunSchtasks(string arguments, bool ignoreMissing = false)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Impossible de démarrer schtasks.exe.");
        process.WaitForExit();
        if (process.ExitCode != 0 && !(ignoreMissing && process.ExitCode == 1))
        {
            throw new InvalidOperationException($"schtasks.exe a échoué (code {process.ExitCode}) : {process.StandardError.ReadToEnd()}");
        }
    }
}
