using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class ClientReleaseSignatureTests
{
    private const string PrivateKeyPem = """
        -----BEGIN EC PRIVATE KEY-----
        MHcCAQEEIELItSsvZN+XIooeE5iykbJT2lzxMYoFgsSsXxtA3OPRoAoGCCqGSM49
        AwEHoUQDQgAEBZL/gR7Ud5zqD2tLqGLGFv0B1MoXNoq6SqgSKbUfHB/ziUYl+bs3
        slIeHa/QwkwxvDi0lgMvzOQFoIih+JNBPQ==
        -----END EC PRIVATE KEY-----
        """;

    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEBZL/gR7Ud5zqD2tLqGLGFv0B1MoX
        Noq6SqgSKbUfHB/ziUYl+bs3slIeHa/QwkwxvDi0lgMvzOQFoIih+JNBPQ==
        -----END PUBLIC KEY-----
        """;

    private const string WrongPublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAErm4k2WQlZ+NoeSgH5GRxW4cQ5x9L
        nyF6CPjS0jV7IYLSG6Bb8LHQ22XHoAR/s6TBWGZbHoMMaBALp8LFXu5alg==
        -----END PUBLIC KEY-----
        """;

    private static ClientReleaseManifest Manifest() =>
        new("0.5.0", new string('a', 64), "/api/v1/client/packages/0.5.0");

    [Fact]
    public void SignThenVerifyRoundTrips()
    {
        var manifest = Manifest();
        var signature = ClientReleaseSignature.Sign(manifest, PrivateKeyPem);
        var signed = new SignedClientReleaseManifest(manifest.Version, manifest.Sha256, manifest.DownloadUrl, signature);

        Assert.True(ClientReleaseSignature.Verify(signed, PublicKeyPem));
    }

    [Fact]
    public void TamperedShaFailsVerification()
    {
        var manifest = Manifest();
        var signature = ClientReleaseSignature.Sign(manifest, PrivateKeyPem);
        var tampered = new SignedClientReleaseManifest(manifest.Version, new string('b', 64), manifest.DownloadUrl, signature);

        Assert.False(ClientReleaseSignature.Verify(tampered, PublicKeyPem));
    }

    [Fact]
    public void WrongPublicKeyFailsVerification()
    {
        var manifest = Manifest();
        var signature = ClientReleaseSignature.Sign(manifest, PrivateKeyPem);
        var signed = new SignedClientReleaseManifest(manifest.Version, manifest.Sha256, manifest.DownloadUrl, signature);

        Assert.False(ClientReleaseSignature.Verify(signed, WrongPublicKeyPem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("YWJj")]
    public void MalformedSignatureFailsWithoutThrowing(string signature)
    {
        var manifest = Manifest();
        var signed = new SignedClientReleaseManifest(manifest.Version, manifest.Sha256, manifest.DownloadUrl, signature);

        Assert.False(ClientReleaseSignature.Verify(signed, PublicKeyPem));
    }
}
