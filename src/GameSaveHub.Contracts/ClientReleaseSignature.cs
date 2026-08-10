using System.Security.Cryptography;
using System.Text;

namespace GameSaveHub.Contracts;

public static class ClientReleaseSignature
{
    public static string Sign(ClientReleaseManifest manifest, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.SignData(CanonicalBytes(manifest), HashAlgorithmName.SHA256));
    }

    public static bool Verify(SignedClientReleaseManifest signedManifest, string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(signedManifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            var manifest = new ClientReleaseManifest(signedManifest.Version, signedManifest.Sha256, signedManifest.DownloadUrl);
            return key.VerifyData(CanonicalBytes(manifest), Convert.FromBase64String(signedManifest.Signature), HashAlgorithmName.SHA256);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static byte[] CanonicalBytes(ClientReleaseManifest manifest) =>
        Encoding.UTF8.GetBytes($"{manifest.Version}\n{manifest.Sha256}\n{manifest.DownloadUrl}");
}
