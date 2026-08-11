using System.Text.Json;

namespace GameSaveHub.Client.Setup;

internal sealed record MachineConfigFile(MachineClientServiceConfig ClientService);

/// <summary>
/// Reprise exacte de la table <c>$config</c> écrite par <c>INSTALL-GAMESAVEHUB-CLIENT.ps1</c>,
/// liée à <c>ClientServiceOptions</c> côté service.
/// </summary>
internal sealed record MachineClientServiceConfig(
    string PipeName,
    string RegisteredUserSid,
    string RegisteredUserLocalAppData,
    string ServerBaseUrl,
    string StatePath,
    string TransferRootPath,
    string ManagedSlotStatePath,
    string CngKeyName,
    bool EnableWgsTransfer);

internal static class MachineConfig
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Valeur du verrou d'écriture WGS déjà en place, ou <c>null</c> si aucune configuration
    /// lisible n'existe. Refermer ce verrou par inadvertance bloquerait un transfert en cours
    /// de campagne sans que rien ne le signale : il doit être relu, jamais supposé.
    /// </summary>
    public static bool? ReadWriteGate(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var existing = JsonSerializer.Deserialize<MachineConfigFile>(File.ReadAllText(path), ReadOptions);
            return existing?.ClientService?.EnableWgsTransfer;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(
        string path,
        string registeredUserSid,
        string registeredUserLocalAppData,
        string serverBaseUrl,
        bool enableWgsTransfer)
    {
        var config = new MachineConfigFile(new MachineClientServiceConfig(
            PipeName: "GameSaveHub.Client",
            RegisteredUserSid: registeredUserSid,
            RegisteredUserLocalAppData: registeredUserLocalAppData,
            ServerBaseUrl: serverBaseUrl,
            StatePath: @"%ProgramData%\GameSaveHub\client-state.json",
            TransferRootPath: @"%ProgramData%\GameSaveHub\transfers",
            ManagedSlotStatePath: @"%ProgramData%\GameSaveHub\managed-slot.json",
            CngKeyName: "GameSaveHub.DeviceIdentity",
            EnableWgsTransfer: enableWgsTransfer));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, WriteOptions));
    }
}
