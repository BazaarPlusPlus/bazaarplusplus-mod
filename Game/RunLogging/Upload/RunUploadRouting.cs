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
            _cachedClientIds ??= ReadStateFromDisk();
            return _cachedClientIds.TryGetValue(scope.Trim(), out var clientId)
                ? clientId
                : null;
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
            _cachedClientIds ??= ReadStateFromDisk();
            _cachedClientIds[scope.Trim()] = clientId.Trim();
            PersistState();
        }
    }

    public void ClearScopedClientId(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Scope is required.", nameof(scope));

        lock (_sync)
        {
            _cachedClientIds ??= ReadStateFromDisk();
            if (!_cachedClientIds.Remove(scope.Trim()))
                return;

            PersistState();
        }
    }

    private Dictionary<string, string> ReadStateFromDisk()
    {
        if (!File.Exists(_statePath))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var payload = JsonConvert.DeserializeObject<RunUploadClientState>(File.ReadAllText(_statePath));
            return payload?.ClientIds != null
                ? new Dictionary<string, string>(payload.ClientIds, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "RunUploadClientStateStore",
                $"Failed to read client state from {_statePath}: {ex.GetType().Name} - {ex.Message}. Resetting to empty state."
            );
            return new Dictionary<string, string>(StringComparer.Ordinal);
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
                new RunUploadClientState { ClientIds = _cachedClientIds },
                Formatting.Indented
            )
        );
    }

    private sealed class RunUploadClientState
    {
        [JsonProperty("client_ids")]
        public Dictionary<string, string>? ClientIds { get; set; }
    }
}
