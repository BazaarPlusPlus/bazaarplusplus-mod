#nullable enable
using System;
using System.Net.Http;
using System.Text;
using BazaarPlusPlus.Game.RunLogging.Upload;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class CombatReplayUploadRequestSigner
{
    private readonly RunUploadKeyStore _keyStore;

    public CombatReplayUploadRequestSigner(RunUploadKeyStore keyStore)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
    }

    public HttpRequestMessage CreateSignedUploadRequest(
        string uploadEndpoint,
        string json,
        string clientId,
        string installId,
        string battleId,
        string? runId
    )
    {
        if (string.IsNullOrWhiteSpace(uploadEndpoint))
            throw new ArgumentException("Upload endpoint is required.", nameof(uploadEndpoint));

        var timestamp = DateTimeOffset.UtcNow.ToString("o");
        var nonce = Guid.NewGuid().ToString("N");
        var bodyHash = RunUploadRequestSigner.ComputeBodyHash(json);
        var canonical = RunUploadRequestSigner.BuildCanonicalRequest(
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
        request.Headers.TryAddWithoutValidation("X-BPP-Battle-Id", battleId);
        if (!string.IsNullOrWhiteSpace(runId))
            request.Headers.TryAddWithoutValidation("X-BPP-Run-Id", runId);
        request.Headers.TryAddWithoutValidation("X-BPP-Plugin-Version", MyPluginInfo.PLUGIN_VERSION);
        request.Headers.TryAddWithoutValidation("X-BPP-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-BPP-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-BPP-Content-SHA256", bodyHash);
        request.Headers.TryAddWithoutValidation("X-BPP-Signature-Alg", "rsa-pkcs1-sha256");
        request.Headers.TryAddWithoutValidation("X-BPP-Signature", signature);
        return request;
    }
}
