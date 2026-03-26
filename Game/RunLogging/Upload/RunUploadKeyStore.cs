#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadKeyStore
{
    private readonly string _privateKeyPath;
    private readonly object _sync = new();
    private RunUploadKeyMaterial? _cachedKeyMaterial;

    public RunUploadKeyStore(string privateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
            throw new ArgumentException("Private key path is required.", nameof(privateKeyPath));

        _privateKeyPath = privateKeyPath;
    }

    public RunUploadKeyMaterial GetOrCreateKeyMaterial()
    {
        lock (_sync)
        {
            if (_cachedKeyMaterial != null)
                return _cachedKeyMaterial;

            if (File.Exists(_privateKeyPath))
            {
                _cachedKeyMaterial = JsonConvert.DeserializeObject<RunUploadKeyMaterial>(
                    File.ReadAllText(_privateKeyPath)
                );
                if (_cachedKeyMaterial != null)
                    return _cachedKeyMaterial;
            }

            using var rsa = RSA.Create();
            rsa.KeySize = 2048;
            var parameters = rsa.ExportParameters(true);
            _cachedKeyMaterial = RunUploadKeyMaterial.FromParameters(parameters);

            var directory = Path.GetDirectoryName(_privateKeyPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                _privateKeyPath,
                JsonConvert.SerializeObject(_cachedKeyMaterial, Formatting.Indented)
            );

            return _cachedKeyMaterial;
        }
    }

    public string Sign(string canonicalString)
    {
        if (canonicalString == null)
            throw new ArgumentNullException(nameof(canonicalString));

        var keyMaterial = GetOrCreateKeyMaterial();
        using var rsa = RSA.Create();
        rsa.ImportParameters(keyMaterial.ToParameters());
        var signatureBytes = rsa.SignData(
            System.Text.Encoding.UTF8.GetBytes(canonicalString),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return Convert.ToBase64String(signatureBytes);
    }
}

internal sealed class RunUploadKeyMaterial
{
    [JsonProperty("algorithm")]
    public string Algorithm { get; set; } = "rsa-pkcs1-sha256";

    [JsonProperty("modulus_b64")]
    public string ModulusBase64 { get; set; } = string.Empty;

    [JsonProperty("exponent_b64")]
    public string ExponentBase64 { get; set; } = string.Empty;

    [JsonProperty("d_b64")]
    public string DBase64 { get; set; } = string.Empty;

    [JsonProperty("p_b64")]
    public string PBase64 { get; set; } = string.Empty;

    [JsonProperty("q_b64")]
    public string QBase64 { get; set; } = string.Empty;

    [JsonProperty("dp_b64")]
    public string DPBase64 { get; set; } = string.Empty;

    [JsonProperty("dq_b64")]
    public string DQBase64 { get; set; } = string.Empty;

    [JsonProperty("inverse_q_b64")]
    public string InverseQBase64 { get; set; } = string.Empty;

    [JsonProperty("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    public RunUploadPublicKey ToPublicKey()
    {
        return new RunUploadPublicKey
        {
            Algorithm = Algorithm,
            ModulusBase64 = ModulusBase64,
            ExponentBase64 = ExponentBase64,
            Fingerprint = Fingerprint,
        };
    }

    public RSAParameters ToParameters()
    {
        return new RSAParameters
        {
            Modulus = Convert.FromBase64String(ModulusBase64),
            Exponent = Convert.FromBase64String(ExponentBase64),
            D = Convert.FromBase64String(DBase64),
            P = Convert.FromBase64String(PBase64),
            Q = Convert.FromBase64String(QBase64),
            DP = Convert.FromBase64String(DPBase64),
            DQ = Convert.FromBase64String(DQBase64),
            InverseQ = Convert.FromBase64String(InverseQBase64),
        };
    }

    public static RunUploadKeyMaterial FromParameters(RSAParameters parameters)
    {
        return new RunUploadKeyMaterial
        {
            ModulusBase64 = Convert.ToBase64String(parameters.Modulus ?? Array.Empty<byte>()),
            ExponentBase64 = Convert.ToBase64String(parameters.Exponent ?? Array.Empty<byte>()),
            DBase64 = Convert.ToBase64String(parameters.D ?? Array.Empty<byte>()),
            PBase64 = Convert.ToBase64String(parameters.P ?? Array.Empty<byte>()),
            QBase64 = Convert.ToBase64String(parameters.Q ?? Array.Empty<byte>()),
            DPBase64 = Convert.ToBase64String(parameters.DP ?? Array.Empty<byte>()),
            DQBase64 = Convert.ToBase64String(parameters.DQ ?? Array.Empty<byte>()),
            InverseQBase64 = Convert.ToBase64String(parameters.InverseQ ?? Array.Empty<byte>()),
            Fingerprint = ComputeFingerprint(parameters),
        };
    }

    private static string ComputeFingerprint(RSAParameters parameters)
    {
        using var sha256 = SHA256.Create();
        var modulus = parameters.Modulus ?? Array.Empty<byte>();
        var exponent = parameters.Exponent ?? Array.Empty<byte>();
        var combined = new byte[modulus.Length + exponent.Length];
        Buffer.BlockCopy(modulus, 0, combined, 0, modulus.Length);
        Buffer.BlockCopy(exponent, 0, combined, modulus.Length, exponent.Length);
        return Convert.ToBase64String(sha256.ComputeHash(combined));
    }
}

internal sealed class RunUploadPublicKey
{
    [JsonProperty("algorithm")]
    public string Algorithm { get; set; } = "rsa-pkcs1-sha256";

    [JsonProperty("modulus_b64")]
    public string ModulusBase64 { get; set; } = string.Empty;

    [JsonProperty("exponent_b64")]
    public string ExponentBase64 { get; set; } = string.Empty;

    [JsonProperty("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;
}
