#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal static class RunUploadScopes
{
    public const string Runs = "Runs";

    public const string Replays = "ReplayCloudflare";
}

internal sealed class RunUploadEndpointSet
{
    public string RegistrationEndpoint { get; set; } = string.Empty;

    public string UploadEndpoint { get; set; } = string.Empty;
}

internal sealed class RunUploadClientStateStore
{
    private readonly string _statePath;
    private readonly object _sync = new();
    private Dictionary<string, string>? _cachedClientIds;
    private Dictionary<string, string>? _cachedBoundPlayerAccountIds;

    public RunUploadClientStateStore(string statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath))
            throw new ArgumentException("State path is required.", nameof(statePath));

        _statePath = statePath;
    }

    public string? TryGetScopedClientId(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Scope is required.", nameof(scope));

        lock (_sync)
        {
            EnsureStateLoaded();
            return _cachedClientIds!.TryGetValue(scope.Trim(), out var clientId) ? clientId : null;
        }
    }

    public void SaveScopedClientId(string scope, string clientId)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Scope is required.", nameof(scope));
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client id is required.", nameof(clientId));

        lock (_sync)
        {
            EnsureStateLoaded();
            _cachedClientIds![scope.Trim()] = clientId.Trim();
            PersistState();
        }
    }

    public void ClearScopedClientId(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Scope is required.", nameof(scope));

        lock (_sync)
        {
            EnsureStateLoaded();
            if (!_cachedClientIds!.Remove(scope.Trim()))
                return;

            PersistState();
        }
    }

    public string? TryGetScopedBoundPlayerAccountId(string scope, string clientId)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Scope is required.", nameof(scope));
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client id is required.", nameof(clientId));

        lock (_sync)
        {
            EnsureStateLoaded();
            return _cachedBoundPlayerAccountIds!.TryGetValue(
                BuildBoundPlayerAccountKey(scope, clientId),
                out var playerAccountId
            )
                ? playerAccountId
                : null;
        }
    }

    public void SaveScopedBoundPlayerAccountId(string scope, string clientId, string playerAccountId)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Scope is required.", nameof(scope));
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client id is required.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(playerAccountId))
            throw new ArgumentException("Player account id is required.", nameof(playerAccountId));

        lock (_sync)
        {
            EnsureStateLoaded();
            _cachedBoundPlayerAccountIds![BuildBoundPlayerAccountKey(scope, clientId)] =
                playerAccountId.Trim();
            PersistState();
        }
    }

    private void EnsureStateLoaded()
    {
        if (_cachedClientIds != null && _cachedBoundPlayerAccountIds != null)
            return;

        var state = ReadStateFromDisk();
        _cachedClientIds = state.ClientIds;
        _cachedBoundPlayerAccountIds = state.BoundPlayerAccountIds;
    }

    private RunUploadClientStatePayload ReadStateFromDisk()
    {
        if (!File.Exists(_statePath))
        {
            return new RunUploadClientStatePayload();
        }
        try
        {
            var payload = JsonConvert.DeserializeObject<RunUploadClientState>(
                File.ReadAllText(_statePath)
            );
            return new RunUploadClientStatePayload(
                payload?.ClientIds != null
                    ? new Dictionary<string, string>(payload.ClientIds, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal),
                payload?.BoundPlayerAccountIds != null
                    ? new Dictionary<string, string>(
                        payload.BoundPlayerAccountIds,
                        StringComparer.Ordinal
                    )
                    : new Dictionary<string, string>(StringComparer.Ordinal)
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "RunUploadClientStateStore",
                $"Failed to read client state from {_statePath}: {ex.GetType().Name} - {ex.Message}. Resetting to empty state."
            );
            return new RunUploadClientStatePayload();
        }
    }

    private void PersistState()
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(
            _statePath,
            JsonConvert.SerializeObject(
                new RunUploadClientState
                {
                    ClientIds = _cachedClientIds,
                    BoundPlayerAccountIds = _cachedBoundPlayerAccountIds,
                },
                Formatting.Indented
            )
        );
    }

    private static string BuildBoundPlayerAccountKey(string scope, string clientId)
    {
        return $"{scope.Trim()}::{clientId.Trim()}";
    }

    private sealed class RunUploadClientState
    {
        [JsonProperty("client_ids")]
        public Dictionary<string, string>? ClientIds { get; set; }

        [JsonProperty("bound_player_account_ids")]
        public Dictionary<string, string>? BoundPlayerAccountIds { get; set; }
    }

    private sealed class RunUploadClientStatePayload
    {
        public RunUploadClientStatePayload()
        {
            ClientIds = new Dictionary<string, string>(StringComparer.Ordinal);
            BoundPlayerAccountIds = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public RunUploadClientStatePayload(
            Dictionary<string, string> clientIds,
            Dictionary<string, string> boundPlayerAccountIds
        )
        {
            ClientIds = clientIds;
            BoundPlayerAccountIds = boundPlayerAccountIds;
        }

        public Dictionary<string, string> ClientIds { get; }

        public Dictionary<string, string> BoundPlayerAccountIds { get; }
    }
}
