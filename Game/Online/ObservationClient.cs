#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.Identity;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Online;

internal sealed class ObservationClient
{
    private readonly HttpClient _httpClient;
    private readonly InstallationRecordStore _installationStore;
    private readonly V3Routes _routes;
    private readonly InstallationRequestSigner _requestSigner;

    public ObservationClient(
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

    public async Task<bool> PublishObservationAsync(
        PlayerObservationRecord observation,
        CancellationToken cancellationToken
    )
    {
        if (!_installationStore.TryLoad(out var installation) || installation == null)
            return false;

        var bodyBytes = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(
                observation,
                V3Serialization.SerializerSettings
            )
        );
        using var request = _requestSigner.CreateRequest(
            HttpMethod.Post,
            _routes.PublishObservation,
            bodyBytes,
            installation,
            DateTimeOffset.UtcNow.ToString("o")
        );
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
