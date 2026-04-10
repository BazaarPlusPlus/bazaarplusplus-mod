#nullable enable
using System;
using System.Net.Http;
using BazaarPlusPlus.Game.Identity;

namespace BazaarPlusPlus.Game.Online;

internal sealed class ModOnlineClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly InstallationRecordStore _installationStore;
    private readonly V3Routes _routes;
    private readonly InstallationRequestSigner _requestSigner;

    public ModOnlineClient(
        HttpClient httpClient,
        InstallationRecordStore installationStore,
        V3Routes routes
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _installationStore =
            installationStore ?? throw new ArgumentNullException(nameof(installationStore));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _requestSigner = new InstallationRequestSigner(_installationStore);
    }

    public ObservationClient CreateObservationClient()
    {
        return new ObservationClient(_httpClient, _installationStore, _routes, _requestSigner);
    }

    public RunBundleClient CreateRunBundleClient()
    {
        return new RunBundleClient(_httpClient, _installationStore, _routes, _requestSigner);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
