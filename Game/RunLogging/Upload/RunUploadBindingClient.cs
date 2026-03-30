#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadBindingClient
{
    private readonly HttpClient _httpClient;
    private readonly RunUploadRequestSigner _requestSigner;
    private readonly string _bindEndpoint;

    public RunUploadBindingClient(
        HttpClient httpClient,
        RunUploadRequestSigner requestSigner,
        string bindEndpoint
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
        if (string.IsNullOrWhiteSpace(bindEndpoint))
            throw new ArgumentException("Bind endpoint is required.", nameof(bindEndpoint));

        _bindEndpoint = bindEndpoint;
    }

    public async Task<RunUploadBindingResult> BindPlayerAccountAsync(
        string clientId,
        string installId,
        string playerAccountId,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(playerAccountId))
            return RunUploadBindingResult.Failure(
                "player_account_id_required",
                shouldFallback: false,
                shouldReRegister: false
            );

        try
        {
            var json = JsonConvert.SerializeObject(
                new JObject
                {
                    ["player_account_id"] = playerAccountId.Trim(),
                    ["observed_player_account_id"] = playerAccountId.Trim(),
                },
                RunUploadSerialization.SerializerSettings
            );

            using var request = _requestSigner.CreateSignedRequest(
                HttpMethod.Post,
                _bindEndpoint,
                json,
                clientId,
                installId,
                "application/json"
            );
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (response.IsSuccessStatusCode)
                return RunUploadBindingResult.Success();

            var responseBody = await response.Content.ReadAsStringAsync();
            var statusCode = (int)response.StatusCode;
            return RunUploadBindingResult.Failure(
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
                "RunUploadBindingClient",
                $"Bind request failed for endpoint={_bindEndpoint}: {ex.GetType().Name} - {ex.Message}"
            );
            return RunUploadBindingResult.Failure(
                RunUploadErrorFormatter.Truncate(ex.Message),
                shouldFallback: true,
                shouldReRegister: false
            );
        }
    }
}

internal readonly struct RunUploadBindingResult : IBppAuthenticatedApiResult
{
    private RunUploadBindingResult(
        bool succeeded,
        string? error,
        bool shouldFallback,
        bool shouldReRegister
    )
    {
        Succeeded = succeeded;
        Error = error;
        ShouldFallback = shouldFallback;
        ShouldReRegister = shouldReRegister;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public bool ShouldFallback { get; }

    public bool ShouldReRegister { get; }

    public static RunUploadBindingResult Success() => new(true, null, false, false);

    public static RunUploadBindingResult Failure(
        string error,
        bool shouldFallback,
        bool shouldReRegister
    ) => new(false, error, shouldFallback, shouldReRegister);
}
