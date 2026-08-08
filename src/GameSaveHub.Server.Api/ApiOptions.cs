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
