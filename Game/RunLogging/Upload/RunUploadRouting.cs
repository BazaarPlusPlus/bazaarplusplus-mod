#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal enum RunUploadMode
{
    Off,
    Auto,
    Global,
    CN,
}

internal enum RunUploadRouteKind
{
    Global,
    CN,
}

internal sealed class RunUploadEndpointSet
{
    public RunUploadRouteKind RouteKind { get; set; }

    public string RegistrationEndpoint { get; set; } = string.Empty;

    public string UploadEndpoint { get; set; } = string.Empty;
}

internal sealed class RunUploadRouteStateStore
{
    private readonly string _statePath;
    private readonly object _sync = new();
    private RunUploadRouteState? _cachedState;

    public RunUploadRouteStateStore(string statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath))
            throw new ArgumentException("State path is required.", nameof(statePath));

        _statePath = statePath;
    }

    public RunUploadRouteState Load()
    {
        lock (_sync)
        {
            _cachedState ??= ReadStateFromDisk();
            return _cachedState.Clone();
        }
    }

    public void Save(RunUploadRouteState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            _cachedState = state.Clone();
            File.WriteAllText(
                _statePath,
                JsonConvert.SerializeObject(_cachedState, Formatting.Indented)
            );
        }
    }

    private RunUploadRouteState ReadStateFromDisk()
    {
        if (!File.Exists(_statePath))
            return new RunUploadRouteState();

        return JsonConvert.DeserializeObject<RunUploadRouteState>(File.ReadAllText(_statePath))
            ?? new RunUploadRouteState();
    }
}

internal sealed class RunUploadRouteState
{
    [JsonProperty("preferred_route")]
    public string? PreferredRoute { get; set; }

    [JsonProperty("preferred_route_expires_at_utc")]
    public DateTimeOffset? PreferredRouteExpiresAtUtc { get; set; }

    [JsonProperty("global_failure_count")]
    public int GlobalFailureCount { get; set; }

    [JsonProperty("cn_failure_count")]
    public int CnFailureCount { get; set; }

    public RunUploadRouteState Clone()
    {
        return new RunUploadRouteState
        {
            PreferredRoute = PreferredRoute,
            PreferredRouteExpiresAtUtc = PreferredRouteExpiresAtUtc,
            GlobalFailureCount = GlobalFailureCount,
            CnFailureCount = CnFailureCount,
        };
    }
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

    public string? TryGetClientId(RunUploadRouteKind routeKind)
    {
        lock (_sync)
        {
            _cachedClientIds ??= ReadStateFromDisk();
            return _cachedClientIds.TryGetValue(routeKind.ToString(), out var clientId)
                ? clientId
                : null;
        }
    }

    public void SaveClientId(RunUploadRouteKind routeKind, string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client id is required.", nameof(clientId));

        lock (_sync)
        {
            _cachedClientIds ??= ReadStateFromDisk();
            _cachedClientIds[routeKind.ToString()] = clientId.Trim();

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
    }

    private Dictionary<string, string> ReadStateFromDisk()
    {
        if (!File.Exists(_statePath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var payload = JsonConvert.DeserializeObject<RunUploadClientState>(File.ReadAllText(_statePath));
        return payload?.ClientIds != null
            ? new Dictionary<string, string>(payload.ClientIds, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private sealed class RunUploadClientState
    {
        [JsonProperty("client_ids")]
        public Dictionary<string, string>? ClientIds { get; set; }
    }
}

internal sealed class RunUploadRouteSelector
{
    private readonly RunUploadMode _mode;
    private readonly RunUploadRouteStateStore _stateStore;
    private readonly int _globalFailureThreshold;
    private readonly TimeSpan _preferredRouteCacheDuration;

    public RunUploadRouteSelector(
        RunUploadMode mode,
        RunUploadRouteStateStore stateStore,
        int globalFailureThreshold,
        TimeSpan preferredRouteCacheDuration
    )
    {
        _mode = mode;
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _globalFailureThreshold = Math.Max(1, globalFailureThreshold);
        _preferredRouteCacheDuration = preferredRouteCacheDuration;
    }

    public IReadOnlyList<RunUploadEndpointSet> GetRouteOrder(
        RunUploadEndpointSet? globalEndpoint,
        RunUploadEndpointSet? cnEndpoint
    )
    {
        return _mode switch
        {
            RunUploadMode.Off => Array.Empty<RunUploadEndpointSet>(),
            RunUploadMode.Global => Filter(globalEndpoint),
            RunUploadMode.CN => Filter(cnEndpoint),
            _ => BuildAutoOrder(globalEndpoint, cnEndpoint),
        };
    }

    public void RecordRouteSuccess(RunUploadRouteKind routeKind)
    {
        var state = _stateStore.Load();
        state.PreferredRoute = routeKind.ToString();
        state.PreferredRouteExpiresAtUtc = DateTimeOffset.UtcNow + _preferredRouteCacheDuration;
        if (routeKind == RunUploadRouteKind.Global)
            state.GlobalFailureCount = 0;
        else
            state.CnFailureCount = 0;
        _stateStore.Save(state);
    }

    public void RecordRouteFailure(RunUploadRouteKind routeKind)
    {
        var state = _stateStore.Load();
        if (routeKind == RunUploadRouteKind.Global)
        {
            state.GlobalFailureCount++;
            if (state.GlobalFailureCount >= _globalFailureThreshold)
            {
                state.PreferredRoute = RunUploadRouteKind.CN.ToString();
                state.PreferredRouteExpiresAtUtc = DateTimeOffset.UtcNow + _preferredRouteCacheDuration;
                state.GlobalFailureCount = 0;
            }
        }
        else
        {
            state.CnFailureCount++;
        }

        _stateStore.Save(state);
    }

    private IReadOnlyList<RunUploadEndpointSet> BuildAutoOrder(
        RunUploadEndpointSet? globalEndpoint,
        RunUploadEndpointSet? cnEndpoint
    )
    {
        var state = _stateStore.Load();
        if (
            string.Equals(state.PreferredRoute, RunUploadRouteKind.CN.ToString(), StringComparison.Ordinal)
            && state.PreferredRouteExpiresAtUtc.HasValue
            && state.PreferredRouteExpiresAtUtc.Value > DateTimeOffset.UtcNow
        )
        {
            return Filter(cnEndpoint, globalEndpoint);
        }

        return Filter(globalEndpoint, cnEndpoint);
    }

    private static IReadOnlyList<RunUploadEndpointSet> Filter(params RunUploadEndpointSet?[] endpointSets)
    {
        var result = new List<RunUploadEndpointSet>();
        foreach (var endpointSet in endpointSets)
        {
            if (endpointSet == null)
                continue;

            result.Add(endpointSet);
        }

        return result;
    }

    public static RunUploadMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return RunUploadMode.Auto;

        return value.Trim().ToUpperInvariant() switch
        {
            "OFF" => RunUploadMode.Off,
            "GLOBAL" => RunUploadMode.Global,
            "CN" => RunUploadMode.CN,
            _ => RunUploadMode.Auto,
        };
    }
}
