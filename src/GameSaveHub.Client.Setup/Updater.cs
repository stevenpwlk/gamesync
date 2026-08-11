using System.Net.Http.Json;
using System.Text.Json;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Setup;

public static class Updater
{
    private const string ServerBaseUrl = "https://saves.stevenpwlk.fr:18443";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // Réparation d'abord, sans condition. C'est de la pure inspection de l'état local
        // du disque : elle ne dépend ni du réseau, ni du jeu, ni d'une session. La placer
        // derrière la récupération du manifeste — comme c'était le cas — rendait un poste
        // coupé en plein renommage irréparable dès que le NAS était injoignable, alors même
        // que c'est précisément le poste qui a le plus besoin d'être réparé.
        if (!TryReconcileInstallFolders())
        {
            Console.Error.WriteLine(
                "Aucune installation détectée (ni Client, ni Client.old) : intervention manuelle requise, " +
                "relancez GameSaveHub-Setup.exe en mode installation.");
            return 1;
        }

        using var http = new HttpClient();

        var manifestResponse = await http.GetAsync($"{ServerBaseUrl}/api/v1/client/latest", cancellationToken);
        if (!manifestResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Vérification de mise à jour impossible ({(int)manifestResponse.StatusCode}), nouvelle tentative à la prochaine exécution planifiée.");
            return 0;
        }
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<SignedClientReleaseManifest>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Manifeste de release illisible.");

        var installedVersion = ReadInstalledVersion();
        if (!UpdateDecision.ShouldApplyUpdate(installedVersion, manifest.Version))
        {
            Console.WriteLine($"Déjà à jour ({installedVersion ?? "version inconnue"}) : la version publiée ({manifest.Version}) n'est pas plus récente.");
            return 0;
        }

        if (!ClientReleaseSignature.Verify(manifest, ClientReleasePublicKey.Pem))
        {
            Console.Error.WriteLine("Signature du manifeste invalide : mise à jour refusée.");
            return 1;
        }

        var stagingRoot = Path.Combine(SetupPaths.ProgramDataRoot, "update-staging");
        Directory.CreateDirectory(stagingRoot);
        var packagePath = Path.Combine(stagingRoot, $"{manifest.Version}.zip");
        var packageBytes = await http.GetByteArrayAsync($"{ServerBaseUrl}{manifest.DownloadUrl}", cancellationToken);
        await File.WriteAllBytesAsync(packagePath, packageBytes, cancellationToken);
        var downloadedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(packageBytes));
        if (!downloadedHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Hash du paquet téléchargé invalide : mise à jour refusée.");
            File.Delete(packagePath);
            return 1;
        }

        var newFolder = Path.Combine(stagingRoot, "Client.new");
        if (Directory.Exists(newFolder)) Directory.Delete(newFolder, recursive: true);
        System.IO.Compression.ZipFile.ExtractToDirectory(packagePath, newFolder);

        // Un paquet incomplet passe hash et signature sans problème : ils prouvent seulement
        // qu'il est bien celui qui a été signé, pas qu'il contient une installation complète.
        // Le découvrir après la bascule laisserait le poste sans service et sans application.
        var missing = FindMissingPackageEntries(newFolder);
        if (missing.Count > 0)
        {
            Console.Error.WriteLine($"Paquet incomplet ({string.Join(", ", missing)}) : mise à jour refusée.");
            Directory.Delete(newFolder, recursive: true);
            File.Delete(packagePath);
            return 1;
        }

        var status = await SetupPipeClient.QueryMaintenanceStatusAsync(cancellationToken);
        if (UpdateDecision.ShouldDeferUpdate(status))
        {
            Console.WriteLine("Mise à jour reportée : condition de sûreté non réunie (jeu ouvert, session active ou transition en cours).");
            return 0;
        }

        // maintenance-status observe le jeu et les transferts, pas l'application GameSave Hub
        // elle-même : c'est pourtant elle qui, ouverte, tient les fichiers de %ProgramFiles%
        // et fait échouer le renommage.
        if (IsClientAppRunning())
        {
            Console.WriteLine("Mise à jour reportée : l'application GameSave Hub est ouverte, fermez-la pour permettre la mise à jour.");
            return 0;
        }

        await ApplySwapAsync(newFolder, cancellationToken);
        Console.WriteLine($"Mise à jour vers {manifest.Version} appliquée.");
        return 0;
    }

    private static string? ReadInstalledVersion() =>
        File.Exists(SetupPaths.InstalledVersionPath) ? File.ReadAllText(SetupPaths.InstalledVersionPath).Trim() : null;

    private static IReadOnlyList<string> FindMissingPackageEntries(string extractedRoot)
    {
        var expected = new[]
        {
            Path.Combine("Service", "GameSaveHub.Client.Service.exe"),
            Path.Combine("App", "GameSaveHub.Client.App.exe"),
            "VERSION"
        };
        return [.. expected.Where(relative => !File.Exists(Path.Combine(extractedRoot, relative)))];
    }

    private static bool IsClientAppRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("GameSaveHub.Client.App");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    /// <summary>
    /// Applique la table de <see cref="FolderSwapReconciler"/> à l'état réel du disque.
    /// Idempotente : après un appel réussi, <c>Client</c> existe et <c>Client.old</c> non.
    /// Renvoie <c>false</c> quand aucun des deux dossiers n'existe, seul cas qu'aucune
    /// réparation automatique ne peut couvrir.
    /// </summary>
    private static bool TryReconcileInstallFolders()
    {
        var installRoot = SetupPaths.InstallRoot;
        var oldFolder = SetupPaths.OldInstallRoot;
        switch (FolderSwapReconciler.Resolve(Directory.Exists(installRoot), Directory.Exists(oldFolder)))
        {
            case FolderSwapReconciliationAction.RestoreFromOld:
                Console.WriteLine("Bascule interrompue détectée : restauration de l'installation précédente.");
                Directory.Move(oldFolder, installRoot);
                return true;
            case FolderSwapReconciliationAction.CleanupOldFolder:
                Console.WriteLine("Reliquat d'une bascule précédente détecté : suppression de Client.old.");
                Directory.Delete(oldFolder, recursive: true);
                return true;
            case FolderSwapReconciliationAction.ManualReviewRequired:
                return false;
            default:
                return true;
        }
    }

    private static async Task ApplySwapAsync(string newFolder, CancellationToken cancellationToken)
    {
        var currentFolder = SetupPaths.InstallRoot;
        var oldFolder = SetupPaths.OldInstallRoot;

        // Le téléchargement a pu durer plusieurs minutes : on revérifie l'état des dossiers
        // juste avant d'y toucher plutôt que de faire confiance au constat du début de run.
        if (!TryReconcileInstallFolders())
            throw new InvalidOperationException("Installation existante introuvable : intervention manuelle requise.");

        MigrateLegacyMachineConfig();

        StopService();
        var installationMovedAside = false;
        try
        {
            Directory.Move(currentFolder, oldFolder);
            installationMovedAside = true;
            Directory.Move(newFolder, currentFolder);
            installationMovedAside = false;
        }
        catch
        {
            // Laisser le PC du joueur avec un service définitivement arrêté est pire qu'une
            // mise à jour non appliquée : on remet l'installation d'origine en place puis on
            // relance le service avant de laisser l'erreur remonter.
            if (installationMovedAside && !Directory.Exists(currentFolder) && Directory.Exists(oldFolder))
                Directory.Move(oldFolder, currentFolder);
            TryStartService();
            throw;
        }

        await StartServiceAndWaitHealthyAsync(cancellationToken);
        Directory.Delete(oldFolder, recursive: true);
    }

    /// <summary>
    /// Reprend la configuration machine d'un poste installé avant le Lot 3, où elle vivait
    /// dans le dossier basculé. Sans cette reprise, la première mise à jour d'un tel poste
    /// emporterait le fichier avec <c>Client.old</c> et le service ne redémarrerait plus.
    /// </summary>
    private static void MigrateLegacyMachineConfig()
    {
        if (File.Exists(SetupPaths.MachineConfigPath) || !File.Exists(SetupPaths.LegacyMachineConfigPath)) return;
        Console.WriteLine("Reprise de la configuration machine héritée vers %ProgramData%\\GameSaveHub.");
        Directory.CreateDirectory(SetupPaths.ProgramDataRoot);
        File.Copy(SetupPaths.LegacyMachineConfigPath, SetupPaths.MachineConfigPath, overwrite: false);
    }

    private static void StopService()
    {
        using var service = new System.ServiceProcess.ServiceController(SetupPaths.ServiceName);
        if (service.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
    }

    private static void TryStartService()
    {
        try
        {
            using var service = new System.ServiceProcess.ServiceController(SetupPaths.ServiceName);
            if (service.Status != System.ServiceProcess.ServiceControllerStatus.Running)
            {
                service.Start();
                service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            Console.Error.WriteLine($"Le service n'a pas pu être relancé automatiquement : {exception.Message}");
        }
    }

    private static async Task StartServiceAndWaitHealthyAsync(CancellationToken cancellationToken)
    {
        using (var service = new System.ServiceProcess.ServiceController(SetupPaths.ServiceName))
        {
            service.Start();
            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }

        // Contrôle de santé réel, et non simple statut du gestionnaire de services :
        // c'est le tube nommé qui porte tout l'usage du client.
        if (!await SetupPipeClient.IsServiceAnsweringAsync(TimeSpan.FromSeconds(30), cancellationToken))
            throw new InvalidOperationException("Le service redémarré ne répond pas sur son tube nommé dans les 30 s.");
    }
}
