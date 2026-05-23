#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.Online.Models;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Online;

internal sealed class BazaarDbScreenshotClient
{
    private readonly HttpClient _httpClient;
    private readonly V3Routes _routes;

    public BazaarDbScreenshotClient(HttpClient httpClient, V3Routes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<BazaarDbScreenshotUploadResult> UploadScreenshotAsync(
        BazaarDbScreenshotUploadRequestV3 payload,
        CancellationToken cancellationToken
    )
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        var bodyBytes = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(payload, V3Serialization.SerializerSettings)
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, _routes.UploadBazaarDbScreenshot)
        {
            Content = new ByteArrayContent(bodyBytes),
        };
        request.Content.Headers.ContentType = new("application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return BazaarDbScreenshotUploadResult.Success();

        var responseBody = await response.Content.ReadAsStringAsync();
        var formattedError = V3ErrorFormatter.FormatHttpFailure(
            (int)response.StatusCode,
            responseBody
        );
        return IsPermanentClientError(response.StatusCode)
            ? BazaarDbScreenshotUploadResult.PermanentFailure(formattedError)
            : BazaarDbScreenshotUploadResult.TransientFailure(formattedError);
    }

    private static bool IsPermanentClientError(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        // 4xx except 408 (Request Timeout) and 429 (Too Many Requests) — design §10.1 maps both to transient.
        return code >= 400 && code < 500 && code != 408 && code != 429;
    }
}

internal readonly struct BazaarDbScreenshotUploadResult
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
