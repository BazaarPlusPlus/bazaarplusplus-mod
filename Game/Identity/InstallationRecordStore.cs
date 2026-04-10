#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;

namespace BazaarPlusPlus.Game.Identity;

internal sealed class InstallationRecordStore
{
    private readonly string _installationRecordPath;
    private readonly string _installationPrivateKeyPath;

    public InstallationRecordStore(string installationRecordPath, string installationPrivateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(installationRecordPath))
            throw new ArgumentException(
                "Installation record path is required.",
                nameof(installationRecordPath)
            );
        if (string.IsNullOrWhiteSpace(installationPrivateKeyPath))
            throw new ArgumentException(
                "Installation private key path is required.",
                nameof(installationPrivateKeyPath)
            );

        _installationRecordPath = installationRecordPath;
        _installationPrivateKeyPath = installationPrivateKeyPath;
    }

    public bool TryLoad(out InstallationRecord? record)
    {
        return BppIdentityEnvelope.TryDecodePayload(_installationRecordPath, out record);
    }

    public bool TryLoadPrivateKey(out RSA rsa)
    {
        rsa = RSA.Create();
        if (!File.Exists(_installationPrivateKeyPath))
            return false;

        try
        {
            var privateKeyBytes = File.ReadAllBytes(_installationPrivateKeyPath);
            rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            return true;
        }
        catch
        {
            rsa.Dispose();
            rsa = RSA.Create();
            return false;
        }
    }

    public void Save(InstallationRecord record, byte[] privateKeyBytes)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));
        if (privateKeyBytes == null)
            throw new ArgumentNullException(nameof(privateKeyBytes));

        var recordDirectory = Path.GetDirectoryName(_installationRecordPath);
        if (!string.IsNullOrWhiteSpace(recordDirectory))
            Directory.CreateDirectory(recordDirectory);

        var keyDirectory = Path.GetDirectoryName(_installationPrivateKeyPath);
        if (!string.IsNullOrWhiteSpace(keyDirectory))
            Directory.CreateDirectory(keyDirectory);

        File.WriteAllBytes(_installationRecordPath, BppIdentityEnvelope.EncodePayload(record));
        File.WriteAllBytes(_installationPrivateKeyPath, privateKeyBytes);
    }
}
