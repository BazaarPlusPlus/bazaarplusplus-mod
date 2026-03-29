#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadApiClient
{
    private readonly HttpClient _httpClient;
    private readonly RunUploadRequestSigner _requestSigner;
    private readonly string _uploadEndpoint;

    public RunUploadApiClient(
        HttpClient httpClient,
        RunUploadRequestSigner requestSigner,
        string uploadEndpoint
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
        if (string.IsNullOrWhiteSpace(uploadEndpoint))
            throw new ArgumentException("Upload endpoint is required.", nameof(uploadEndpoint));

        _uploadEndpoint = uploadEndpoint;
    }

    public async Task<RunUploadApiResult> UploadRunAsync(
        string json,
        string clientId,
        string installId,
        string runId,
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
                runId
            );
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (response.IsSuccessStatusCode)
                return RunUploadApiResult.Success();

            var responseBody = await response.Content.ReadAsStringAsync();
            var statusCode = (int)response.StatusCode;
            return RunUploadApiResult.Failure(
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
            BppLog.Warn(
                "RunUploadApiClient",
                $"Run upload request failed for endpoint={_uploadEndpoint}: {FormatException(ex)}"
            );
            return RunUploadApiResult.Failure(
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

internal readonly struct RunUploadApiResult : IBppAuthenticatedApiResult
{
    private RunUploadApiResult(
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

    public static RunUploadApiResult Success() => new(true, null, false, false);

    public static RunUploadApiResult Failure(
        string error,
        bool shouldFallback,
        bool shouldReRegister
    ) => new(false, error, shouldFallback, shouldReRegister);
}
