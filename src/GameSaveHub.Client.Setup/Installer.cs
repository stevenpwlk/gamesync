using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.ServiceProcess;
using GameSaveHub.Client.Orchestration;
using Microsoft.Win32;

namespace GameSaveHub.Client.Setup;

[SupportedOSPlatform("windows")]
public static class Installer
{
    private const string ServiceName = SetupPaths.ServiceName;

    public static async Task<int> RunAsync(string serverBaseUrl, CancellationToken cancellationToken)
    {
        var installRoot = SetupPaths.InstallRoot;
        var serviceRoot = Path.Combine(installRoot, "Service");
        var appRoot = Path.Combine(installRoot, "App");
        var programDataRoot = SetupPaths.ProgramDataRoot;

        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("L'installation doit être lancée en tant qu'administrateur.");

        // Le compte à enregistrer est celui du joueur assis devant le PC, pas celui du
        // processus d'installation élevé : une élévation UAC par un autre compte
        // administrateur enregistrerait le mauvais profil WGS.
        var interactiveUser = ResolveInteractiveUserName();
        var sid = TranslateToSid(interactiveUser);
        if (ServiceAccountGuard.IsReservedAccount(sid))
            throw new InvalidOperationException("Le compte joueur ne peut pas être LocalSystem/LocalService/NetworkService.");
        var localAppData = ResolveLocalAppData(interactiveUser, sid);

        Console.WriteLine($"Utilisateur joueur : {interactiveUser}");
        Console.WriteLine($"SID joueur         : {sid}");
        Console.WriteLine($"Profil AppData     : {localAppData}");

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

        // État du verrou d'écriture AVANT cette installation, relu à ses deux emplacements
        // possibles : le nouveau (%ProgramData%) et celui du script PowerShell historique.
        var previousGate = MachineConfig.ReadWriteGate(SetupPaths.MachineConfigPath)
            ?? MachineConfig.ReadWriteGate(SetupPaths.LegacyMachineConfigPath);

        Directory.CreateDirectory(serviceRoot);
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(programDataRoot);

        // Le payload est le dossier livré à côté de l'exécutable, pas quelque chose qui y est
        // embarqué : la copie installée dans %ProgramFiles% (celle que vise la tâche planifiée)
        // ne le contient volontairement pas, puisque --auto-update télécharge le sien. Lancer
        // cette copie en mode installation doit donc le dire clairement plutôt que d'échouer
        // sur un chemin introuvable.
        var payloadRoot = Path.Combine(AppContext.BaseDirectory, "payload");
        if (!Directory.Exists(payloadRoot))
        {
            throw new InvalidOperationException(
                $"Dossier « payload » introuvable à côté de l'exécutable ({payloadRoot}). " +
                "Lancez GameSaveHub-Setup.exe depuis le dossier d'installation livré, tel quel, sans déplacer l'exécutable seul.");
        }
        CopyDirectory(Path.Combine(payloadRoot, "Service"), serviceRoot);
        CopyDirectory(Path.Combine(payloadRoot, "App"), appRoot);
        WriteInstalledVersion(Path.Combine(payloadRoot, "VERSION"));

        var serviceExe = SetupPaths.ServiceExePath(installRoot);
        var appExe = SetupPaths.AppExePath(installRoot);
        if (!File.Exists(serviceExe)) throw new InvalidOperationException($"EXE service absent : {serviceExe}");
        if (!File.Exists(appExe)) throw new InvalidOperationException($"EXE application absent : {appExe}");

        // Le verrou serveur (FeatureGates:AllowHostTransfer) est la vraie barrière de
        // production ; celui-ci n'est qu'une seconde précaution locale, utile tant que
        // Lot 3 tournait en préflight. Un poste neuf (sans config précédente, donc jamais
        // installé même en Lot 2) l'ouvre désormais directement : un joueur qui reçoit ce
        // paquet doit pouvoir jouer sans édition manuelle de fichier. Sur une réinstallation,
        // la valeur déjà en place est reprise telle quelle plutôt que modifiée en silence —
        // ça reste la seule façon de ne pas casser, ni rouvrir à son insu, un poste existant.
        var enableWgsTransfer = previousGate ?? true;
        MachineConfig.Write(SetupPaths.MachineConfigPath, sid, localAppData, serverBaseUrl, enableWgsTransfer);

        var managedSlotAlreadyBound = File.Exists(Path.Combine(programDataRoot, "managed-slot.json"));

        InstallService(serviceExe);
        using (var service = new ServiceController(ServiceName))
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }

        // « Running » côté SCM ne prouve pas que le service fonctionne : son PipeServerWorker
        // peut avoir échoué au démarrage (RegisteredUserSid absent, par exemple) sans que le
        // processus s'arrête. Seule une réponse sur le tube nommé le prouve.
        if (!await SetupPipeClient.IsServiceAnsweringAsync(TimeSpan.FromSeconds(15), cancellationToken))
        {
            throw new InvalidOperationException(
                "Le service a démarré mais ne répond pas sur son tube nommé « GameSaveHub.Client » dans les 15 s. " +
                "Installation interrompue : consultez le journal d'événements Windows (source GameSaveHubClient).");
        }

        CreateStartMenuShortcut(appExe, appRoot);

        // La tâche planifiée doit viser une copie stable de l'installateur : l'exécutable
        // lancé par le joueur est en général dans son dossier Téléchargements, qu'il videra.
        var installedSetupExe = CopySelfToInstallRoot();
        ScheduledTaskManager.Register(installedSetupExe);

        Console.WriteLine();
        Console.WriteLine("INSTALLATION RÉUSSIE");
        Console.WriteLine($"Service : {ServiceName} / Running (tube nommé opérationnel)");
        Console.WriteLine($"Version installée : {ReadPayloadVersion(Path.Combine(payloadRoot, "VERSION"))}");
        Console.WriteLine($"Application : {appExe}");
        Console.WriteLine($"Configuration machine : {SetupPaths.MachineConfigPath}");
        Console.WriteLine($"Mise à jour automatique : {installedSetupExe} --auto-update");
        Console.WriteLine(enableWgsTransfer
            ? "Écriture des sauvegardes : ACTIVÉE sur ce PC (EnableWgsTransfer=true)."
            : "Écriture des sauvegardes : DÉSACTIVÉE (EnableWgsTransfer=false).");
        if (previousGate is not null)
        {
            Console.WriteLine(previousGate.Value
                ? "Verrou d'écriture : valeur ouverte conservée depuis l'installation précédente."
                : "Verrou d'écriture : valeur fermée conservée depuis l'installation précédente.");
        }
        else
        {
            Console.WriteLine("Verrou d'écriture : ouvert par défaut (premier poste, aucune configuration précédente).");
        }
        Console.WriteLine(managedSlotAlreadyBound
            ? "Slot local permanent : déjà enregistré sur ce PC (conservé lors de cette installation)."
            : "Slot local permanent : pas encore configuré. L'application proposera la configuration initiale.");

        return 0;
    }

    /// <summary>
    /// Nom du compte Windows réellement connecté, via la même source que le script
    /// PowerShell historique (<c>Win32_ComputerSystem.UserName</c>). Passe par
    /// <c>powershell.exe</c> plutôt que par <c>System.Management</c> : ce paquet embarque
    /// des dépendances natives (WMI interop) dont l'extraction dans un exécutable
    /// mono-fichier auto-contenu est une source connue d'échecs au premier lancement,
    /// alors que <c>powershell.exe</c> est présent sur toute installation de Windows 11.
    /// </summary>
    private static string ResolveInteractiveUserName()
    {
        using var process = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -NonInteractive -Command \"(Get-CimInstance Win32_ComputerSystem).UserName\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Impossible de démarrer powershell.exe pour identifier l'utilisateur interactif.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException(
                "Aucun utilisateur Windows interactif détecté : ouvrez une session sur le compte du joueur avant d'installer.");
        }
        return output;
    }

    private static string TranslateToSid(string userName)
    {
        try
        {
            return ((SecurityIdentifier)new NTAccount(userName).Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (IdentityNotMappedException)
        {
            throw new InvalidOperationException($"Compte Windows introuvable : {userName}");
        }
    }

    private static string ResolveLocalAppData(string userName, string sid)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        var profilePath = key?.GetValue("ProfileImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (string.IsNullOrWhiteSpace(profilePath))
            throw new InvalidOperationException($"Profil Windows introuvable dans le registre pour {userName} ({sid}).");
        var localAppData = Path.Combine(Environment.ExpandEnvironmentVariables(profilePath), "AppData", "Local");
        if (!Directory.Exists(localAppData))
            throw new InvalidOperationException($"AppData\\Local introuvable pour {userName} : {localAppData}");
        return localAppData;
    }

    /// <summary>
    /// Écrit <c>%ProgramFiles%\GameSaveHub\Client\VERSION</c>, que l'updater relit pour
    /// décider s'il y a quelque chose à installer. Sans ce fichier, chaque exécution de
    /// <c>--auto-update</c> retéléchargeait et rebasculait la version déjà en place.
    /// </summary>
    private static void WriteInstalledVersion(string payloadVersionPath) =>
        File.WriteAllText(SetupPaths.InstalledVersionPath, ReadPayloadVersion(payloadVersionPath));

    private static string ReadPayloadVersion(string payloadVersionPath)
    {
        if (!File.Exists(payloadVersionPath)) return SetupPaths.CurrentVersion;
        var version = File.ReadAllText(payloadVersionPath).Trim();
        return string.IsNullOrWhiteSpace(version) ? SetupPaths.CurrentVersion : version;
    }

    private static string CopySelfToInstallRoot()
    {
        var source = Environment.ProcessPath
            ?? throw new InvalidOperationException("Chemin de l'exécutable d'installation introuvable.");
        var destination = SetupPaths.InstalledSetupExePath;
        Directory.CreateDirectory(SetupPaths.ProductRoot);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, overwrite: true);
        return destination;
    }

    private static void InstallService(string serviceExePath)
    {
        RunSc($"create {ServiceName} binPath= \"{serviceExePath}\" DisplayName= \"GameSave Hub Client\" start= delayed-auto");
        RunSc($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/15000//0");
    }

    private static void RunSc(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments)
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
