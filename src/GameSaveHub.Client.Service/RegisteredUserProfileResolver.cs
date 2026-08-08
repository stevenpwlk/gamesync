using System.Security.Principal;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace GameSaveHub.Client.Service;

public sealed class RegisteredUserProfileResolver(IOptions<ClientServiceOptions> options)
{
    private readonly ClientServiceOptions _options = options.Value;

    public string ResolveLocalApplicationData()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("La résolution du profil joueur nécessite Windows.");
        }
        var sidText = _options.RegisteredUserSid.Trim();
        if (string.IsNullOrWhiteSpace(sidText))
        {
            throw new InvalidOperationException("ClientService:RegisteredUserSid est obligatoire pour résoudre le stockage WGS du joueur.");
        }
        var sid = new SecurityIdentifier(sidText);
        if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.LocalServiceSid) || sid.IsWellKnown(WellKnownSidType.NetworkServiceSid))
        {
            throw new InvalidOperationException("Le SID enregistré doit être celui d'un utilisateur joueur, jamais un compte de service Windows.");
        }

        if (!string.IsNullOrWhiteSpace(_options.RegisteredUserLocalAppData))
        {
            return ValidateLocalAppData(_options.RegisteredUserLocalAppData);
        }

        var keyPath = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid.Value}";
        using var profileKey = Registry.LocalMachine.OpenSubKey(keyPath, writable: false)
            ?? throw new InvalidOperationException($"Profil Windows introuvable pour le SID {sid.Value}.");
        var profileImage = profileKey.GetValue("ProfileImagePath") as string;
        if (string.IsNullOrWhiteSpace(profileImage))
        {
            throw new InvalidOperationException($"ProfileImagePath absent pour le SID {sid.Value}.");
        }
        var profileRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(profileImage));
        return ValidateLocalAppData(Path.Combine(profileRoot, "AppData", "Local"));
    }

    private static string ValidateLocalAppData(string path)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"AppData\\Local du joueur introuvable : {fullPath}");
        }
        return fullPath;
    }
}
