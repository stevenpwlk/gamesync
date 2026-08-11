namespace GameSaveHub.Client.Setup;

/// <summary>
/// Clé publique compilée servant à vérifier chaque manifeste de release avant application
/// (spec Lot 3 §4). Clé de production réelle de Steven, générée le 11 août 2026 en dehors
/// du dépôt. La clé privée correspondante ne doit jamais être committée ni déployée sur le
/// NAS : seule cette clé publique a sa place ici, et la même valeur doit être configurée
/// dans `GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM` côté NAS pour que `client-release publish`
/// accepte les manifestes signés avec la clé privée correspondante.
/// </summary>
public static class ClientReleasePublicKey
{
    public const string Pem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAESBIY4RvL0Bsc+n2yPe09dCUT1zOP
        ZDsMYRD6BhiSm/xptvZjDZ/mtDNCFDXuvp1sMhHDrFSJabeo8F8lkwTj3A==
        -----END PUBLIC KEY-----
        """;
}
