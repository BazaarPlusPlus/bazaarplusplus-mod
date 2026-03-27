#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.RunLogging.Upload;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class CombatReplayUploadApiClient
{
    private readonly HttpClient _httpClient;
    private readonly CombatReplayUploadRequestSigner _requestSigner;
    private readonly string _uploadEndpoint;

    public CombatReplayUploadApiClient(
        HttpClient httpClient,
        CombatReplayUploadRequestSigner requestSigner,
        string uploadEndpoint
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
        if (string.IsNullOrWhiteSpace(uploadEndpoint))
            throw new ArgumentException("Upload endpoint is required.", nameof(uploadEndpoint));

        _uploadEndpoint = uploadEndpoint;
    }

    public async Task<CombatReplayUploadApiResult> UploadReplayAsync(
        string json,
        string clientId,
        string installId,
        string battleId,
        string? runId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var request = _requestSigner.CreateSignedUploadRequest(
                _uploadEndpoint,
                json,
                clientId,
                installId,
                battleId,
                runId
            );
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            var responseBody = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                string? objectKey = null;
                if (!string.IsNullOrWhiteSpace(responseBody))
                {
                    try
                    {
                        objectKey = JObject.Parse(responseBody)["object_key"]?.Value<string>()?.Trim();
                    }
                    catch
                    {
                        objectKey = null;
                    }
                }

                return CombatReplayUploadApiResult.Success(objectKey);
            }

            var statusCode = (int)response.StatusCode;
            return CombatReplayUploadApiResult.Failure(
                $"http_{statusCode}:{RunUploadErrorFormatter.Truncate(responseBody)}",
                shouldFallback: statusCode >= 500 || statusCode == 429,
                shouldReRegister: statusCode == 401
                    || statusCode == 403
                    || (
                        statusCode == 404
                        && RunUploadErrorFormatter.IndicatesMissingClient(responseBody)
                    )
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CombatReplayUploadApiResult.Failure(
                RunUploadErrorFormatter.Truncate(ex.Message),
                shouldFallback: true,
                shouldReRegister: false
            );
        }
    }
}

internal readonly struct CombatReplayUploadApiResult
{
    private CombatReplayUploadApiResult(
        bool succeeded,
        string? error,
        bool shouldFallback,
        bool shouldReRegister,
        string? objectKey
    )
    {
        Succeeded = succeeded;
        Error = error;
        ShouldFallback = shouldFallback;
        ShouldReRegister = shouldReRegister;
        ObjectKey = objectKey;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public bool ShouldFallback { get; }

    public bool ShouldReRegister { get; }

    public string? ObjectKey { get; }

    public static CombatReplayUploadApiResult Success(string? objectKey) =>
        new(true, null, false, false, objectKey);

    public static CombatReplayUploadApiResult Failure(
        string error,
        bool shouldFallback,
        bool shouldReRegister
    ) => new(false, error, shouldFallback, shouldReRegister, null);
}
