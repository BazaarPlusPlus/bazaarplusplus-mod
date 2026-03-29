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
    private readonly string _clientStateScope;
    private readonly string _purpose;
    private readonly string _registrationEndpoint;

    public RunUploadRegistrationClient(
        HttpClient httpClient,
        RunUploadClientStateStore clientStateStore,
        RunUploadKeyStore keyStore,
        string clientStateScope,
        string purpose,
        string registrationEndpoint
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _clientStateStore =
            clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        if (string.IsNullOrWhiteSpace(clientStateScope))
            throw new ArgumentException(
                "Client state scope is required.",
                nameof(clientStateScope)
            );
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Purpose is required.", nameof(purpose));

        _clientStateScope = clientStateScope.Trim();
        _purpose = purpose.Trim();
        if (string.IsNullOrWhiteSpace(registrationEndpoint))
            throw new ArgumentException(
                "Registration endpoint is required.",
                nameof(registrationEndpoint)
            );

        _registrationEndpoint = registrationEndpoint;
    }

    public async Task<string?> EnsureClientRegistrationAsync(
        string installId,
        CancellationToken cancellationToken
    )
    {
        var existingClientId = _clientStateStore.TryGetScopedClientId(_clientStateScope);
        if (!string.IsNullOrWhiteSpace(existingClientId))
        {
            BppLog.Info(
                "RunUploadRegistrationClient",
                $"Using cached client id for scope={_clientStateScope}: {existingClientId}."
            );
            return existingClientId;
        }

        try
        {
            var keyMaterial = _keyStore.GetOrCreateKeyMaterial();
            var pluginVersion = BppPluginVersion.Current;
            BppLog.Info(
                "RunUploadRegistrationClient",
                $"Registering client for scope={_clientStateScope}, purpose={_purpose}, fingerprint={keyMaterial.Fingerprint}."
            );
            var requestBody = JsonConvert.SerializeObject(
                new JObject
                {
                    ["install_id"] = installId,
                    ["plugin_version"] = pluginVersion,
                    ["purpose"] = _purpose,
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
            request.Headers.TryAddWithoutValidation("X-BPP-Plugin-Version", pluginVersion);

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

            _clientStateStore.SaveScopedClientId(_clientStateScope, clientId);
            BppLog.Info(
                "RunUploadRegistrationClient",
                $"Client registration succeeded for scope={_clientStateScope}: {clientId}."
            );
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
                $"Client registration failed for endpoint={_registrationEndpoint}: {FormatException(ex)}"
            );
            return null;
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
