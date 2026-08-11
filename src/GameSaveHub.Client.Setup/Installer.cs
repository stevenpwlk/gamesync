using System.Runtime.Versioning;
using System.ServiceProcess;
using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.Client.Setup;

[SupportedOSPlatform("windows")]
public static class Installer
{
    private const string ServiceName = "GameSaveHubClient";

    public static async Task<int> RunAsync(string serverBaseUrl, CancellationToken cancellationToken)
    {
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameSaveHub", "Client");
        var serviceRoot = Path.Combine(installRoot, "Service");
        var appRoot = Path.Combine(installRoot, "App");
        var programDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameSaveHub");

        var principal = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("L'installation doit être lancée en tant qu'administrateur.");

        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Identité Windows introuvable.");
        if (ServiceAccountGuard.IsReservedAccount(sid))
            throw new InvalidOperationException("Le compte joueur ne peut pas être LocalSystem/LocalService/NetworkService.");

        using (var existing = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == ServiceName))
        {
            if (existing is not null)
            {
                Console.WriteLine("Arrêt de l'ancienne version du service...");
                if (existing.Status != ServiceControllerStatus.Stopped)
                {
                    existing.Stop();
                    existing.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }
                RunSc($"delete {ServiceName}");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        Directory.CreateDirectory(serviceRoot);
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(programDataRoot);

        var payloadRoot = Path.Combine(AppContext.BaseDirectory, "payload");
        CopyDirectory(Path.Combine(payloadRoot, "Service"), serviceRoot);
        CopyDirectory(Path.Combine(payloadRoot, "App"), appRoot);

        var serviceExe = Path.Combine(serviceRoot, "GameSaveHub.Client.Service.exe");
        var appExe = Path.Combine(appRoot, "GameSaveHub.Client.App.exe");
        if (!File.Exists(serviceExe)) throw new InvalidOperationException($"EXE service absent : {serviceExe}");
        if (!File.Exists(appExe)) throw new InvalidOperationException($"EXE application absent : {appExe}");

        var managedSlotAlreadyBound = File.Exists(Path.Combine(programDataRoot, "managed-slot.json"));

        InstallService(serviceExe);
        using (var service = new ServiceController(ServiceName))
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }

        CreateStartMenuShortcut(appExe, appRoot);
        ScheduledTaskManager.Register(Path.Combine(AppContext.BaseDirectory, "GameSaveHub-Setup.exe"));

        Console.WriteLine();
        Console.WriteLine("INSTALLATION RÉUSSIE");
        Console.WriteLine($"Service : {ServiceName} / Running");
        Console.WriteLine($"Application : {appExe}");
        Console.WriteLine(managedSlotAlreadyBound
            ? "Slot local permanent : déjà enregistré sur ce PC (conservé lors de cette installation)."
            : "Slot local permanent : pas encore configuré. L'application proposera la configuration initiale.");

        await Task.CompletedTask;
        return 0;
    }

    private static void InstallService(string serviceExePath)
    {
        RunSc($"create {ServiceName} binPath= \"{serviceExePath}\" DisplayName= \"GameSave Hub Client\" start= delayed-auto");
        RunSc($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/15000//0");
    }

    private static void RunSc(string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sc.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Impossible de démarrer sc.exe.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"sc.exe a échoué (code {process.ExitCode}) : {process.StandardError.ReadToEnd()}");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CreateStartMenuShortcut(string appExe, string appRoot)
    {
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
        Directory.CreateDirectory(startMenu);
        var shortcutPath = Path.Combine(startMenu, "GameSave Hub.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = appExe;
        shortcut.WorkingDirectory = appRoot;
        shortcut.Description = "GameSave Hub";
        shortcut.Save();
    }
}
