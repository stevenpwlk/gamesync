using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.Client.Setup;

public sealed record SetupPipeRequest(string Command);
public sealed record SetupPipeResponse(bool Success, string Code, string Message, JsonElement? Data);

/// <summary>
/// Accès en lecture seule au tube nommé du service client, partagé par les trois modes de
/// <c>GameSaveHub-Setup.exe</c>. Le même code de requête servait auparavant, dupliqué, dans
/// <see cref="Updater"/> et <see cref="Uninstaller"/> ; <see cref="Installer"/> en a besoin
/// à son tour pour son contrôle de santé après démarrage du service.
/// </summary>
internal static class SetupPipeClient
{
    private const string PipeName = "GameSaveHub.Client";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Interroge <c>maintenance-status</c>. Renvoie <c>null</c> si le service ne répond pas
    /// ou répond en échec : l'appelant traite alors la situation comme « non sûr ».
    /// </summary>
    public static async Task<MaintenanceSafetyStatus?> QueryMaintenanceStatusAsync(CancellationToken cancellationToken)
    {
        SetupPipeResponse? response;
        try
        {
            response = await SendAsync("maintenance-status", TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or JsonException)
        {
            return null;
        }
        if (response is null || !response.Success || response.Data is null) return null;
        return response.Data.Value.Deserialize<MaintenanceSafetyStatus>(JsonOptions);
    }

    /// <summary>
    /// Contrôle de santé réel après démarrage du service : « Running » côté SCM ne prouve
    /// rien, le <c>BackgroundService</c> qui porte le tube peut avoir échoué juste après.
    /// Toute réponse formée prouve que <c>PipeServerWorker</c> tourne, y compris un refus
    /// <c>client_not_authorized</c> ; un refus d'ACL à la connexion le prouve aussi, puisque
    /// le tube n'existe que si le worker l'a créé.
    /// </summary>
    public static async Task<bool> IsServiceAnsweringAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync("maintenance-status", timeout, cancellationToken) is not null;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return false;
        }
    }

    private static async Task<SetupPipeResponse?> SendAsync(
        string command,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync((int)connectTimeout.TotalMilliseconds, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }

        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(
            JsonSerializer.Serialize(new SetupPipeRequest(command), JsonOptions).AsMemory(),
            cancellationToken);
        var line = await reader.ReadLineAsync(cancellationToken);
        return line is null ? null : JsonSerializer.Deserialize<SetupPipeResponse>(line, JsonOptions);
    }
}
