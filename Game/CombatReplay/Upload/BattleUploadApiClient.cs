#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.RunLogging.Upload;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class BattleUploadApiClient
{
    private readonly HttpClient _httpClient;
    private readonly BattleUploadRequestSigner _requestSigner;
    private readonly string _uploadEndpoint;

    public BattleUploadApiClient(
        HttpClient httpClient,
        BattleUploadRequestSigner requestSigner,
        string uploadEndpoint
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
        if (string.IsNullOrWhiteSpace(uploadEndpoint))
            throw new ArgumentException("Upload endpoint is required.", nameof(uploadEndpoint));

        _uploadEndpoint = uploadEndpoint;
    }

    public async Task<BattleUploadApiResult> UploadBattleAsync(
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
                        objectKey = JObject
                            .Parse(responseBody)["object_key"]
                            ?.Value<string>()
                            ?.Trim();
                    }
                    catch
                    {
                        objectKey = null;
                    }
                }

                return BattleUploadApiResult.Success(objectKey);
            }

            var statusCode = (int)response.StatusCode;
            return BattleUploadApiResult.Failure(
                RunUploadErrorFormatter.FormatHttpFailure(statusCode, responseBody),
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
            BppLog.Warn(
                "BattleUploadApiClient",
                $"Battle upload request failed for endpoint={_uploadEndpoint}: {FormatException(ex)}"
            );
            return BattleUploadApiResult.Failure(
                RunUploadErrorFormatter.Truncate(ex.Message),
                shouldFallback: true,
                shouldReRegister: false
            );
        }
    }

    private static string FormatException(Exception ex)
    {
        var message = $"{ex.GetType().Name} - {ex.Message}";
        if (ex.InnerException == null)
            return message;

        return $"{message} | Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";
    }
}

internal readonly struct BattleUploadApiResult : IBppAuthenticatedApiResult
{
    private BattleUploadApiResult(
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

    public static BattleUploadApiResult Success(string? objectKey) =>
        new(true, null, false, false, objectKey);

    public static BattleUploadApiResult Failure(
        string error,
        bool shouldFallback,
        bool shouldReRegister
    ) => new(false, error, shouldFallback, shouldReRegister, null);
}
