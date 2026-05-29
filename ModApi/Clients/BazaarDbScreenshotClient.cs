#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class BazaarDbScreenshotClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public BazaarDbScreenshotClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<BazaarDbScreenshotUploadResult> UploadScreenshotAsync(
        BazaarDbScreenshotUploadRequest payload,
        CancellationToken cancellationToken
    )
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        var result = await ModApiJsonPost.PostJsonAsync(
            _httpClient,
            _routes.UploadBazaarDbScreenshot,
            payload,
            cancellationToken
        );
        if (result.IsSuccess)
            return BazaarDbScreenshotUploadResult.Success();

        var formattedError = ModApiErrorFormatter.FormatHttpFailure(
            result.StatusCode,
            result.FailureBody!
        );
        return IsPermanentClientError(result.StatusCode)
            ? BazaarDbScreenshotUploadResult.PermanentFailure(formattedError)
            : BazaarDbScreenshotUploadResult.TransientFailure(formattedError);
    }

    private static bool IsPermanentClientError(int statusCode)
    {
        // 4xx except 408 (Request Timeout) and 429 (Too Many Requests) — design §10.1 maps both to transient.
        return statusCode >= 400 && statusCode < 500 && statusCode != 408 && statusCode != 429;
    }
}

public readonly struct BazaarDbScreenshotUploadResult
{
    private BazaarDbScreenshotUploadResult(bool succeeded, bool permanent, string? error)
    {
        Succeeded = succeeded;
        Permanent = permanent;
        Error = error;
    }

    public bool Succeeded { get; }

    public bool Permanent { get; }

    public string? Error { get; }

    public static BazaarDbScreenshotUploadResult Success() => new(true, false, null);

    public static BazaarDbScreenshotUploadResult TransientFailure(string error) =>
        new(false, false, error);

    public static BazaarDbScreenshotUploadResult PermanentFailure(string error) =>
        new(false, true, error);
}
