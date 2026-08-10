namespace GameSaveHub.Contracts;

public sealed record ClientReleaseManifest(string Version, string Sha256, string DownloadUrl);

public sealed record SignedClientReleaseManifest(string Version, string Sha256, string DownloadUrl, string Signature);
