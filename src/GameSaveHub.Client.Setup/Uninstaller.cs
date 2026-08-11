using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Client.Service;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Client.Setup;

public static class Uninstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var status = await QueryMaintenanceStatusAsync(cancellationToken);
        if (status is not null && !status.SafeToUpdate)
        {
            Console.Error.WriteLine("Désinstallation refusée : une session locale est active ou une transition est en cours. Fermez le jeu et attendez la fin de l'opération en cours avant de réessayer.");
            return 1;
        }

        var revoked = await TryRevokeSelfAsync(cancellationToken);
        Console.WriteLine(revoked
            ? "Appareil révoqué côté serveur."
            : "Révocation côté serveur impossible (hors ligne ou serveur injoignable) : Steven devra révoquer cet appareil manuellement.");

        StopAndRemoveService();
        ScheduledTaskManager.Remove();

        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameSaveHub", "Client");
        if (Directory.Exists(installRoot)) Directory.Delete(installRoot, recursive: true);
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "GameSave Hub.lnk");
        if (File.Exists(shortcutPath)) File.Delete(shortcutPath);

        var programDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameSaveHub");
        if (Directory.Exists(programDataRoot)) Directory.Delete(programDataRoot, recursive: true);

        Console.WriteLine();
        Console.WriteLine("DÉSINSTALLATION TERMINÉE");
        Console.WriteLine("Service, application, tâche planifiée et identité locale supprimés.");
        if (!revoked)
            Console.WriteLine("RAPPEL : contactez Steven pour la révocation manuelle côté serveur.");
        return 0;
    }

    private static async Task<bool> TryRevokeSelfAsync(CancellationToken cancellationToken)
    {
        var options = Options.Create(new ClientServiceOptions());
        var identity = new DeviceIdentity(options);
        if (!identity.Exists) return true; // Rien à révoquer : jamais enrôlé ou déjà nettoyé.

        using var http = new HttpClient();
        var stateStore = new ClientStateStore(options);
        using var client = new AuthenticatedTransferServerClient(http, options, identity, stateStore);
        return await client.RevokeSelfAsync(cancellationToken);
    }

    private static void StopAndRemoveService()
    {
        var service = System.ServiceProcess.ServiceController.GetServices()
            .FirstOrDefault(s => s.ServiceName == "GameSaveHubClient");
        if (service is null) return;
        if (service.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        service.Dispose();
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sc.exe", "delete GameSaveHubClient")
        {
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Impossible de démarrer sc.exe.");
        process.WaitForExit();
    }

    private static async Task<MaintenanceSafetyStatus?> QueryMaintenanceStatusAsync(CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", "GameSaveHub.Client", PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(3000, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new SetupPipeRequest("maintenance-status"), JsonOptions).AsMemory(), cancellationToken);
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null) return null;
        var response = JsonSerializer.Deserialize<SetupPipeResponse>(line, JsonOptions);
        if (response is null || !response.Success || response.Data is null) return null;
        return response.Data.Value.Deserialize<GameSaveHub.Client.Orchestration.MaintenanceSafetyStatus>(JsonOptions);
    }
}
