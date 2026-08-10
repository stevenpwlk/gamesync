namespace GameSaveHub.Server.Api;

public sealed class AuthenticationOptions
{
    public string Issuer { get; set; } = "GameSaveHub";
    public string Audience { get; set; } = "GameSaveHub.Client";
    public string SigningKey { get; set; } = string.Empty;
}

public sealed class FeatureGateOptions
{
    public bool AllowHostTransfer { get; set; }
}

public sealed class ClientCompatibilityOptions
{
    /// <summary>
    /// Vide par défaut : non contraignant. N'est relevée qu'après vérification que les
    /// deux clients pilotes envoient déjà l'en-tête de version requis.
    /// </summary>
    public string? MinimumAcquireVersion { get; set; }
}
