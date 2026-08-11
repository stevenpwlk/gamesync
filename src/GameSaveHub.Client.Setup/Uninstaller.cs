using GameSaveHub.Client.Orchestration;
using GameSaveHub.Client.Service;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Client.Setup;

public static class Uninstaller
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var status = await SetupPipeClient.QueryMaintenanceStatusAsync(cancellationToken);
        if (status is not null && !status.SafeToUpdate)
        {
            Console.Error.WriteLine("Désinstallation refusée : une session locale est active ou une transition est en cours. Fermez le jeu et attendez la fin de l'opération en cours avant de réessayer.");
            return 1;
        }

        var revocation = await TryRevokeSelfAsync(cancellationToken);
        switch (revocation)
        {
            case RevokeSelfOutcome.ActiveSessionBlocked:
                // Le serveur voit une session active que la vérification locale n'a pas vue :
                // continuer supprimerait l'identité qui permet encore de terminer, ou de
                // libérer proprement, cette session. On s'arrête avant toute suppression.
                Console.Error.WriteLine("Désinstallation refusée : le serveur signale une session encore active pour cet appareil.");
                Console.Error.WriteLine("Terminez ou abandonnez la session en cours dans GameSave Hub, puis relancez la désinstallation.");
                Console.Error.WriteLine("Rien n'a été supprimé sur ce PC.");
                return 1;
            case RevokeSelfOutcome.Revoked:
                Console.WriteLine("Appareil révoqué côté serveur.");
                break;
            case RevokeSelfOutcome.Unreachable:
                Console.WriteLine("Révocation côté serveur impossible (hors ligne ou serveur injoignable) : Steven devra révoquer cet appareil manuellement.");
                break;
        }

        StopAndRemoveService();
        ScheduledTaskManager.Remove();

        var installRoot = SetupPaths.InstallRoot;
        if (Directory.Exists(installRoot)) Directory.Delete(installRoot, recursive: true);
        if (Directory.Exists(SetupPaths.OldInstallRoot)) Directory.Delete(SetupPaths.OldInstallRoot, recursive: true);
        if (File.Exists(SetupPaths.InstalledSetupExePath)) TryDeleteInstalledSetupExe();
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "GameSave Hub.lnk");
        if (File.Exists(shortcutPath)) File.Delete(shortcutPath);

        var programDataRoot = SetupPaths.ProgramDataRoot;
        if (Directory.Exists(programDataRoot)) Directory.Delete(programDataRoot, recursive: true);

        Console.WriteLine();
        Console.WriteLine("DÉSINSTALLATION TERMINÉE");
        Console.WriteLine("Service, application, tâche planifiée et identité locale supprimés.");
        if (revocation == RevokeSelfOutcome.Unreachable)
            Console.WriteLine("RAPPEL : contactez Steven pour la révocation manuelle côté serveur.");
        return 0;
    }

    private static async Task<RevokeSelfOutcome> TryRevokeSelfAsync(CancellationToken cancellationToken)
    {
        var options = Options.Create(new ClientServiceOptions());
        var identity = new DeviceIdentity(options);
        if (!identity.Exists) return RevokeSelfOutcome.Revoked; // Rien à révoquer : jamais enrôlé ou déjà nettoyé.

        using var http = new HttpClient();
        var stateStore = new ClientStateStore(options);
        using var client = new AuthenticatedTransferServerClient(http, options, identity, stateStore);
        return await client.RevokeSelfAsync(cancellationToken);
    }

    /// <summary>
    /// La copie installée de l'installateur peut être l'exécutable en train de tourner :
    /// Windows refuse alors de la supprimer, ce qui ne doit pas faire échouer la
    /// désinstallation. Elle est alors marquée pour suppression au prochain démarrage.
    /// </summary>
    private static void TryDeleteInstalledSetupExe()
    {
        try
        {
            File.Delete(SetupPaths.InstalledSetupExePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Copie de l'installateur encore verrouillée, à supprimer manuellement : {SetupPaths.InstalledSetupExePath}");
        }
    }

    private static void StopAndRemoveService()
    {
        var service = System.ServiceProcess.ServiceController.GetServices()
            .FirstOrDefault(s => s.ServiceName == SetupPaths.ServiceName);
        if (service is null) return;
        if (service.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        service.Dispose();
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sc.exe", $"delete {SetupPaths.ServiceName}")
        {
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Impossible de démarrer sc.exe.");
        process.WaitForExit();
    }
}
