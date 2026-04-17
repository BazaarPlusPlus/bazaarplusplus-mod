#nullable enable
using System;
using System.Net.Http;
using BazaarPlusPlus.Game.Identity;

namespace BazaarPlusPlus.Game.Online;

internal sealed class ModOnlineClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly V3Routes _routes;
    private BearerState _bearer = new BearerState();

    public ModOnlineClient(HttpClient httpClient, V3Routes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public HttpClient HttpClient => _httpClient;

    public V3Routes Routes => _routes;

    public BearerState Bearer => _bearer;

    public void LoadBearerFrom(AuthStore authStore, string observedPlayerAccountId)
    {
        if (authStore == null)
            throw new ArgumentNullException(nameof(authStore));

        if (!authStore.TryLoad(out var auth) || auth == null)
        {
            if (_bearer.IsAvailable)
                BppLog.Info("ModOnlineClient", "identity: no auth row; clearing bearer.");
            _bearer = new BearerState();
            return;
        }

        if (
            string.IsNullOrWhiteSpace(observedPlayerAccountId)
            || !string.Equals(
                auth.PlayerAccountId,
                observedPlayerAccountId,
                StringComparison.Ordinal
            )
        )
        {
            if (_bearer.IsAvailable)
                BppLog.Warn(
                    "ModOnlineClient",
                    $"identity: auth.player_account_id='{auth.PlayerAccountId}' != observed='{observedPlayerAccountId}'; skipping bearer until re-login via Installer."
                );
            _bearer = new BearerState();
            return;
        }

        var wasAvailable = _bearer.IsAvailable;
        _bearer = new BearerState(auth.Token, auth.PlayerAccountId, auth.PlayerUsername);
        if (!wasAvailable)
            BppLog.Info("ModOnlineClient", $"identity: logged in as {auth.PlayerUsername}.");
    }

    public void HandleUnauthorized(AuthStore authStore)
    {
        if (authStore == null)
            throw new ArgumentNullException(nameof(authStore));

        authStore.Delete();
        _bearer = new BearerState();
        BppLog.Warn("ModOnlineClient", "identity: received 401, cleared local auth row.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
