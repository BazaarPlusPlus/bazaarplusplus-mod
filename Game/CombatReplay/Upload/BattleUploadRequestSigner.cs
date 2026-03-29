#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using BazaarPlusPlus.Game.RunLogging.Upload;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class BattleUploadRequestSigner
{
    private readonly RunUploadRequestSigner _requestSigner;

    public BattleUploadRequestSigner(RunUploadKeyStore keyStore)
    {
        _requestSigner = new RunUploadRequestSigner(
            keyStore ?? throw new ArgumentNullException(nameof(keyStore))
        );
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

        var extraHeaders = new Dictionary<string, string?> { ["X-BPP-Battle-Id"] = battleId };
        if (!string.IsNullOrWhiteSpace(runId))
            extraHeaders["X-BPP-Run-Id"] = runId;

        return _requestSigner.CreateSignedRequest(
            HttpMethod.Post,
            uploadEndpoint,
            json,
            clientId,
            installId,
            "application/json",
            extraHeaders
        );
    }
}
