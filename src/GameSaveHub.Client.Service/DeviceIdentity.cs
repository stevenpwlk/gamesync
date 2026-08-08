using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Client.Service;

public sealed class DeviceIdentity(IOptions<ClientServiceOptions> options)
{
    private readonly string _keyName = options.Value.CngKeyName;

    public bool Exists => CngKey.Exists(_keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey);

    public string GetOrCreatePublicKeyPem()
    {
        using var key = OpenOrCreate();
        using var signer = new ECDsaCng(key);
        return signer.ExportSubjectPublicKeyInfoPem();
    }

    public byte[] Sign(ReadOnlySpan<byte> payload)
    {
        using var key = OpenExisting();
        using var signer = new ECDsaCng(key);
        return signer.SignData(payload, HashAlgorithmName.SHA256);
    }

    private CngKey OpenOrCreate()
    {
        if (Exists) return OpenExisting();
        var creation = new CngKeyCreationParameters
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
            KeyCreationOptions = CngKeyCreationOptions.MachineKey
        };
        return CngKey.Create(CngAlgorithm.ECDsaP256, _keyName, creation);
    }

    private CngKey OpenExisting() => CngKey.Open(
        _keyName,
        CngProvider.MicrosoftSoftwareKeyStorageProvider,
        CngKeyOpenOptions.MachineKey);
}
