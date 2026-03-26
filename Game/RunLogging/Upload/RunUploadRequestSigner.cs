#nullable enable
using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadRequestSigner
{
    private readonly RunUploadKeyStore _keyStore;

    public RunUploadRequestSigner(RunUploadKeyStore keyStore)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
    }

    public HttpRequestMessage CreateSignedUploadRequest(
        string uploadEndpoint,
        string json,
        string clientId,
        string installId,
        string runId
    )
    {
        if (string.IsNullOrWhiteSpace(uploadEndpoint))
            throw new ArgumentException("Upload endpoint is required.", nameof(uploadEndpoint));

        var timestamp = DateTimeOffset.UtcNow.ToString("o");
        var nonce = Guid.NewGuid().ToString("N");
        var bodyHash = ComputeBodyHash(json);
        var canonical = BuildCanonicalRequest(
            "POST",
            new Uri(uploadEndpoint).AbsolutePath,
            clientId,
            installId,
            timestamp,
            nonce,
            bodyHash
        );
        var signature = _keyStore.Sign(canonical);

        var request = new HttpRequestMessage(HttpMethod.Post, uploadEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-BPP-Client-Id", clientId);
        request.Headers.TryAddWithoutValidation("X-BPP-Install-Id", installId);
        request.Headers.TryAddWithoutValidation("X-BPP-Run-Id", runId);
        request.Headers.TryAddWithoutValidation("X-BPP-Plugin-Version", MyPluginInfo.PLUGIN_VERSION);
        request.Headers.TryAddWithoutValidation("X-BPP-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-BPP-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-BPP-Content-SHA256", bodyHash);
        request.Headers.TryAddWithoutValidation("X-BPP-Signature-Alg", "rsa-pkcs1-sha256");
        request.Headers.TryAddWithoutValidation("X-BPP-Signature", signature);
        return request;
    }

    internal static string BuildCanonicalRequest(
        string method,
        string absolutePath,
        string clientId,
        string installId,
        string timestamp,
        string nonce,
        string bodyHash
    )
    {
        return string.Join(
            "\n",
            new[]
            {
                method.Trim().ToUpperInvariant(),
                NormalizePath(absolutePath),
                clientId.Trim(),
                installId.Trim(),
                timestamp.Trim(),
                nonce.Trim(),
                bodyHash.Trim(),
            }
        );
    }

    internal static string ComputeBodyHash(string json)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(json)));
    }

    private static string NormalizePath(string absolutePath)
    {
        return string.IsNullOrWhiteSpace(absolutePath) ? "/" : absolutePath.Trim();
    }
}
