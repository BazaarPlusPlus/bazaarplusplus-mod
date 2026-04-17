#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Online;
using UnityEngine;

namespace BazaarPlusPlus.Game.Identity;

internal sealed class PlayerObservationController : MonoBehaviour
{
    private const float PollIntervalSeconds = 5f;

    private PlayerObservationStore? _store;
    private AuthStore? _authStore;
    private ModOnlineClient? _onlineClient;
    private float _nextPollAt;
    private string? _lastPlayerAccountId;
    private string? _lastPlayerUsername;

    internal void Configure(
        PlayerObservationStore store,
        AuthStore authStore,
        ModOnlineClient onlineClient
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
        _onlineClient = onlineClient ?? throw new ArgumentNullException(nameof(onlineClient));
    }

    private void Update()
    {
        if (_store == null || _authStore == null || _onlineClient == null)
            return;
        if (Time.unscaledTime < _nextPollAt)
            return;

        _nextPollAt = Time.unscaledTime + PollIntervalSeconds;

        try
        {
            var playerAccountId = BppClientCacheBridge.TryGetProfileAccountId()?.Trim();
            var playerUsername = BppClientCacheBridge.TryGetProfileUsername()?.Trim();
            if (
                string.IsNullOrWhiteSpace(playerAccountId)
                || string.IsNullOrWhiteSpace(playerUsername)
            )
                return;

            var hasRow = _store.TryLoad(out _);
            var shouldRewrite =
                !string.Equals(_lastPlayerAccountId, playerAccountId, StringComparison.Ordinal)
                || !string.Equals(_lastPlayerUsername, playerUsername, StringComparison.Ordinal)
                || !hasRow;
            if (shouldRewrite)
            {
                _store.Save(
                    new PlayerObservationRecord(
                        playerAccountId,
                        playerUsername,
                        DateTimeOffset.UtcNow.ToString("o")
                    )
                );
                BppLog.Info(
                    "PlayerObservationController",
                    $"Wrote player observation for account {playerAccountId} to identity.db."
                );
            }

            _onlineClient.LoadBearerFrom(_authStore, playerAccountId);

            _lastPlayerAccountId = playerAccountId;
            _lastPlayerUsername = playerUsername;
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "PlayerObservationController",
                "Failed while updating player observation.",
                ex
            );
        }
    }
}
