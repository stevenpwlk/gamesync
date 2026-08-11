namespace GameSaveHub.Client.Setup;

/// <summary>
/// Clé publique compilée servant à vérifier chaque manifeste de release avant application
/// (spec Lot 3 §4). Cette valeur est celle de la paire de test générée pour Task 1 de ce
/// plan — elle DOIT être remplacée par la clé publique réelle de Steven avant toute
/// publication réelle (voir Task 13, phase de validation externe). Ne jamais committer la
/// clé privée correspondante : seule la clé publique a sa place ici.
/// </summary>
public static class ClientReleasePublicKey
{
    public const string Pem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEBZL/gR7Ud5zqD2tLqGLGFv0B1MoX
        Noq6SqgSKbUfHB/ziUYl+bs3slIeHa/QwkwxvDi0lgMvzOQFoIih+JNBPQ==
        -----END PUBLIC KEY-----
        """;
}
