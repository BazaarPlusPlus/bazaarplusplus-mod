#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.Identity;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Online;

internal sealed class RunBundleClient
{
    private static readonly UriKind EndpointUriKind = UriKind.Absolute;

    private readonly HttpClient _httpClient;
    private readonly InstallationRecordStore _installationStore;
    private readonly V3Routes _routes;
    private readonly InstallationRequestSigner _requestSigner;

    public RunBundleClient(
        HttpClient httpClient,
        InstallationRecordStore installationStore,
        V3Routes routes,
        InstallationRequestSigner requestSigner
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _installationStore =
            installationStore ?? throw new ArgumentNullException(nameof(installationStore));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
    }

    public async Task<RunBundleUploadResult> UploadRunBundleAsync(
        Models.RunBundleUploadRequestV3 payload,
        CancellationToken cancellationToken
    )
    {
        var bodyBytes = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(
                payload,
                V3Serialization.SerializerSettings
            )
        );
        using var request = CreateUploadRequest(bodyBytes);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return RunBundleUploadResult.Success();

        var responseBody = await response.Content.ReadAsStringAsync();
        return RunBundleUploadResult.Failure(
            V3ErrorFormatter.FormatHttpFailure(
                (int)response.StatusCode,
                responseBody
            )
        );
    }

    private HttpRequestMessage CreateUploadRequest(byte[] bodyBytes)
    {
        if (_installationStore.TryLoad(out var installation) && installation != null)
        {
            return _requestSigner.CreateRequest(
                HttpMethod.Post,
                _routes.UploadRunBundle,
                bodyBytes,
                installation,
                DateTimeOffset.UtcNow.ToString("o")
            );
        }

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_routes.UploadRunBundle, EndpointUriKind)
        )
        {
            Content = new ByteArrayContent(bodyBytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        return request;
    }
}

internal readonly struct RunBundleUploadResult
{
    private RunBundleUploadResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static RunBundleUploadResult Success() => new(true, null);

    public static RunBundleUploadResult Failure(string error) => new(false, error);
}
