#nullable enable
using System.Net;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class BundleUploadClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public BundleUploadClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<BundleUploadResponse> UploadAsync(
        Stream sealedFile,
        long contentLength,
        string contentDigest,
        string expectedBundleId,
        string expectedRunId,
        CancellationToken cancellationToken
    )
    {
        if (sealedFile == null)
            throw new ArgumentNullException(nameof(sealedFile));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _routes.UploadBundle);
            request.Content = new StreamContent(sealedFile);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(
                Bundle.BundleLimitsV5.BundleContentType
            );
            request.Content.Headers.ContentLength = contentLength;
            request.Headers.TryAddWithoutValidation("Content-Digest", contentDigest);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var requestId = response.Headers.TryGetValues("X-Request-Id", out var requestIds)
                ? requestIds.FirstOrDefault()
                : null;
            var retryAfterSeconds =
                response.Headers.RetryAfter?.Delta is { } retryDelta
                    ? Math.Max(0, (int)Math.Ceiling(retryDelta.TotalSeconds))
                : response.Headers.RetryAfter?.Date is { } retryDate
                    ? Math.Max(
                        0,
                        (int)Math.Ceiling((retryDate - DateTimeOffset.UtcNow).TotalSeconds)
                    )
                : (int?)null;
            return Classify(
                response.StatusCode,
                body,
                expectedBundleId,
                expectedRunId,
                requestId,
                retryAfterSeconds
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BundleUploadResponse.Transient("request_timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BundleUploadResponse.Transient(
                "network_error",
                ModApiErrorFormatter.Truncate(ex.Message)
            );
        }
    }

    private static BundleUploadResponse Classify(
        HttpStatusCode status,
        string body,
        string expectedBundleId,
        string expectedRunId,
        string? requestId,
        int? retryAfterSeconds
    )
    {
        var statusCode = (int)status;
        if (statusCode is 200 or 201)
        {
            try
            {
                var receipt = JObject.Parse(body);
                var bundleId = receipt["bundle_id"]?.Value<string>();
                var runId = receipt["run_id"]?.Value<string>();
                var outcome = receipt["outcome"]?.Value<string>();
                var delivery = receipt["bazaardb_delivery"]?.Value<string>();
                var validOutcome = statusCode == 201 ? outcome == "stored" : outcome == "duplicate";
                var validDelivery = delivery is "created" or "existing" or "not_applicable";
                if (
                    bundleId != expectedBundleId
                    || runId != expectedRunId
                    || !validOutcome
                    || !validDelivery
                )
                    return BundleUploadResponse.Transient(
                        "invalid_success_receipt",
                        requestId: requestId
                    );
                return BundleUploadResponse.Uploaded(outcome!, requestId);
            }
            catch
            {
                return BundleUploadResponse.Transient(
                    "invalid_success_receipt",
                    requestId: requestId
                );
            }
        }

        var envelope = TryReadError(body);
        var code = envelope.Code ?? $"http_{statusCode}";
        if (statusCode == 409 && code == "run_already_bundled")
            return BundleUploadResponse.Equivalent(code, requestId);
        if (statusCode == 409 && code == "bundle_id_conflict")
            return BundleUploadResponse.Permanent(code, envelope.Message, requestId);
        if (statusCode is 400 or 411 or 413 or 415 or 422)
            return BundleUploadResponse.Permanent(code, envelope.Message, requestId);
        if (statusCode is 408 or 429 || statusCode >= 500 || envelope.Retryable == true)
            return BundleUploadResponse.Transient(
                code,
                envelope.Message,
                requestId,
                retryAfterSeconds
            );
        if (envelope.HasEnvelope)
            return envelope.Retryable == false
                ? BundleUploadResponse.Permanent(code, envelope.Message, requestId)
                : BundleUploadResponse.Transient(
                    code,
                    envelope.Message,
                    requestId,
                    retryAfterSeconds
                );
        return BundleUploadResponse.Transient(code, requestId: requestId);
    }

    private static ErrorEnvelope TryReadError(string body)
    {
        try
        {
            var error = JObject.Parse(body)["error"] as JObject;
            if (error == null)
                return default;
            return new ErrorEnvelope(
                true,
                error["code"]?.Value<string>(),
                error["message"]?.Value<string>(),
                error["retryable"]?.Value<bool?>()
            );
        }
        catch
        {
            return default;
        }
    }

    private readonly struct ErrorEnvelope
    {
        internal ErrorEnvelope(bool hasEnvelope, string? code, string? message, bool? retryable)
        {
            HasEnvelope = hasEnvelope;
            Code = code;
            Message = message;
            Retryable = retryable;
        }

        internal bool HasEnvelope { get; }
        internal string? Code { get; }
        internal string? Message { get; }
        internal bool? Retryable { get; }
    }
}

public enum BundleUploadDisposition
{
    Uploaded,
    Equivalent,
    Permanent,
    Transient,
}

public sealed class BundleUploadResponse
{
    private BundleUploadResponse(
        BundleUploadDisposition disposition,
        string code,
        string? detail,
        string? requestId,
        string? outcome,
        int? retryAfterSeconds
    )
    {
        Disposition = disposition;
        Code = code;
        Detail = detail;
        RequestId = requestId;
        Outcome = outcome;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public BundleUploadDisposition Disposition { get; }
    public string Code { get; }
    public string? Detail { get; }
    public string? RequestId { get; }
    public string? Outcome { get; }
    public int? RetryAfterSeconds { get; }

    public static BundleUploadResponse Uploaded(string outcome, string? requestId) =>
        new(BundleUploadDisposition.Uploaded, outcome, null, requestId, outcome, null);

    public static BundleUploadResponse Equivalent(string code, string? requestId) =>
        new(BundleUploadDisposition.Equivalent, code, null, requestId, code, null);

    public static BundleUploadResponse Permanent(
        string code,
        string? detail = null,
        string? requestId = null
    ) => new(BundleUploadDisposition.Permanent, code, detail, requestId, null, null);

    public static BundleUploadResponse Transient(
        string code,
        string? detail = null,
        string? requestId = null,
        int? retryAfterSeconds = null
    ) => new(BundleUploadDisposition.Transient, code, detail, requestId, null, retryAfterSeconds);
}
