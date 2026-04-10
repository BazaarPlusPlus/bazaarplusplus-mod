#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.Identity;

internal sealed class PlayerObservationController : MonoBehaviour
{
    private const float PollIntervalSeconds = 5f;

    private PlayerObservationStore? _store;
    private string? _observationPath;
    private float _nextPollAt;
    private string? _lastPlayerAccountId;
    private string? _lastPlayerUsername;

    private void Awake()
    {
        try
        {
            _observationPath = BppRuntimeHost.Paths.PlayerObservationPath;
            if (string.IsNullOrWhiteSpace(_observationPath))
            {
                BppLog.Warn(
                    "PlayerObservationController",
                    "Player observation writer is disabled because the observation path is unavailable."
                );
                return;
            }

            _store = new PlayerObservationStore(_observationPath);
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "PlayerObservationController",
                "Failed to initialize player observation writer.",
                ex
            );
        }
    }

    private void Update()
    {
        if (_store == null || Time.unscaledTime < _nextPollAt)
            return;

        _nextPollAt = Time.unscaledTime + PollIntervalSeconds;

        try
        {
            var playerAccountId = BppClientCacheBridge.TryGetProfileAccountId()?.Trim();
            var playerUsername = BppClientCacheBridge.TryGetProfileUsername()?.Trim();
            if (string.IsNullOrWhiteSpace(playerAccountId) || string.IsNullOrWhiteSpace(playerUsername))
                return;

            var shouldRewrite =
                !string.Equals(_lastPlayerAccountId, playerAccountId, StringComparison.Ordinal)
                || !string.Equals(_lastPlayerUsername, playerUsername, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(_observationPath)
                || !File.Exists(_observationPath);
            if (!shouldRewrite)
                return;

            _store.Save(
                new PlayerObservationRecord
                {
                    PlayerAccountId = playerAccountId,
                    PlayerUsername = playerUsername,
                    ObservedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                }
            );

            _lastPlayerAccountId = playerAccountId;
            _lastPlayerUsername = playerUsername;
            BppLog.Info(
                "PlayerObservationController",
                $"Wrote player observation for account {playerAccountId} to {_observationPath}."
            );
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
