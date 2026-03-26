#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadRegistrationClient
{
    private readonly HttpClient _httpClient;
    private readonly RunUploadClientStateStore _clientStateStore;
    private readonly RunUploadKeyStore _keyStore;
    private readonly RunUploadRouteKind _routeKind;
    private readonly string _registrationEndpoint;

    public RunUploadRegistrationClient(
        HttpClient httpClient,
        RunUploadClientStateStore clientStateStore,
        RunUploadKeyStore keyStore,
        RunUploadRouteKind routeKind,
        string registrationEndpoint
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _clientStateStore = clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _routeKind = routeKind;
        if (string.IsNullOrWhiteSpace(registrationEndpoint))
            throw new ArgumentException("Registration endpoint is required.", nameof(registrationEndpoint));

        _registrationEndpoint = registrationEndpoint;
    }

    public async Task<string?> EnsureClientRegistrationAsync(
        string installId,
        CancellationToken cancellationToken
    )
    {
        var existingClientId = _clientStateStore.TryGetClientId(_routeKind);
        if (!string.IsNullOrWhiteSpace(existingClientId))
            return existingClientId;

        try
        {
            var keyMaterial = _keyStore.GetOrCreateKeyMaterial();
            var requestBody = JsonConvert.SerializeObject(
                new JObject
                {
                    ["install_id"] = installId,
                    ["plugin_version"] = MyPluginInfo.PLUGIN_VERSION,
                    ["requested_at_utc"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["public_key"] = JToken.FromObject(keyMaterial.ToPublicKey()),
                },
                RunUploadSerialization.SerializerSettings
            );
            using var request = new HttpRequestMessage(HttpMethod.Post, _registrationEndpoint)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("X-BPP-Install-Id", installId);
            request.Headers.TryAddWithoutValidation("X-BPP-Plugin-Version", MyPluginInfo.PLUGIN_VERSION);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                BppLog.Warn(
                    "RunUploadRegistrationClient",
                    $"Client registration failed: {(int)response.StatusCode} - {RunUploadErrorFormatter.Truncate(responseBody)}"
                );
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var payload = JObject.Parse(responseJson);
            var clientId = payload["client_id"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                BppLog.Warn(
                    "RunUploadRegistrationClient",
                    "Client registration response did not contain client_id."
                );
                return null;
            }

            _clientStateStore.SaveClientId(_routeKind, clientId);
            return clientId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "RunUploadRegistrationClient",
                $"Client registration failed: {ex.GetType().Name} - {ex.Message}"
            );
            return null;
        }
    }
}
