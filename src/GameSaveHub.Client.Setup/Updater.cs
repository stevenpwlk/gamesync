using System.IO.Pipes;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Setup;

public sealed record SetupPipeRequest(string Command);
public sealed record SetupPipeResponse(bool Success, string Code, string Message, JsonElement? Data);

public static class Updater
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        // ... construction of AuthenticatedTransferServerClient happens once the manifest is verified, see Step 3.

        var manifestResponse = await http.GetAsync("https://saves.stevenpwlk.fr:18443/api/v1/client/latest", cancellationToken);
        if (!manifestResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Vérification de mise à jour impossible ({(int)manifestResponse.StatusCode}), nouvelle tentative à la prochaine exécution planifiée.");
            return 0;
        }
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<SignedClientReleaseManifest>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Manifeste de release illisible.");

        var installedVersion = ReadInstalledVersion();
        if (string.Equals(manifest.Version, installedVersion, StringComparison.Ordinal))
        {
            Console.WriteLine($"Déjà à jour ({installedVersion}).");
            return 0;
        }

        if (!ClientReleaseSignature.Verify(manifest, ClientReleasePublicKey.Pem))
        {
            Console.Error.WriteLine("Signature du manifeste invalide : mise à jour refusée.");
            return 1;
        }

        var programDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameSaveHub");
        var stagingRoot = Path.Combine(programDataRoot, "update-staging");
        Directory.CreateDirectory(stagingRoot);
        var packagePath = Path.Combine(stagingRoot, $"{manifest.Version}.zip");
        var packageBytes = await http.GetByteArrayAsync($"https://saves.stevenpwlk.fr:18443{manifest.DownloadUrl}", cancellationToken);
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

        var status = await QueryMaintenanceStatusAsync(cancellationToken);
        if (status is null || !status.SafeToUpdate)
        {
            Console.WriteLine("Mise à jour reportée : condition de sûreté non réunie (jeu ouvert, session active ou transition en cours).");
            return 0;
        }

        ApplySwap(newFolder);
        Console.WriteLine($"Mise à jour vers {manifest.Version} appliquée.");
        return 0;
    }

    private static string? ReadInstalledVersion() =>
        File.Exists(InstalledVersionPath()) ? File.ReadAllText(InstalledVersionPath()).Trim() : null;

    private static string InstalledVersionPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameSaveHub", "Client", "VERSION");

    private static void ApplySwap(string newFolder)
    {
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameSaveHub", "Client");
        var currentFolder = installRoot;
        var oldFolder = installRoot + ".old";

        var action = FolderSwapReconciler.Resolve(Directory.Exists(currentFolder), Directory.Exists(oldFolder));
        switch (action)
        {
            case FolderSwapReconciliationAction.RestoreFromOld:
                Directory.Move(oldFolder, currentFolder);
                break;
            case FolderSwapReconciliationAction.CleanupOldFolder:
                Directory.Delete(oldFolder, recursive: true);
                break;
            case FolderSwapReconciliationAction.ManualReviewRequired:
                throw new InvalidOperationException("Installation existante introuvable : intervention manuelle requise.");
            case FolderSwapReconciliationAction.NoActionNeeded:
                break;
        }

        StopService();
        Directory.Move(currentFolder, oldFolder);
        Directory.Move(newFolder, currentFolder);
        StartServiceAndWaitHealthy();
        Directory.Delete(oldFolder, recursive: true);
    }

    private static void StopService()
    {
        using var service = new System.ServiceProcess.ServiceController("GameSaveHubClient");
        if (service.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
    }

    private static void StartServiceAndWaitHealthy()
    {
        using var service = new System.ServiceProcess.ServiceController("GameSaveHubClient");
        service.Start();
        service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
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
        return response.Data.Value.Deserialize<MaintenanceSafetyStatus>(JsonOptions);
    }
}
