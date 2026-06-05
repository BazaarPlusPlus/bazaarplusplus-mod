#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentRuntimeController : IDisposable
{
    private const double ListenerReconcileDebounceSeconds = 0.5;
    private const double SnapshotPublishIntervalSeconds = 1.5;

    private readonly IBazaarAgentOptions _options;
    private readonly IBazaarAgentContextReader _contextReader;
    private readonly IBazaarAgentActionDispatcher _dispatcher;
    private readonly IBazaarAgentLogger _logger;
    private readonly IBazaarAgentClock _clock;
    private readonly Action? _snapshotPublished;
    private readonly BazaarAgentContextSnapshotPublisher _snapshots = new();
    private readonly JsonSerializerSettings _responseJson = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    private double _lastTickTime = double.NegativeInfinity;
    private double _lastActionTime = double.NegativeInfinity;
    private double _lastListenerReconcileTime = double.NegativeInfinity;
    private BazaarAgentDecisionLog? _decisionLog;
    private BazaarAgentHttpServer? _http;
    private BazaarAgentActionQueue? _queue;
    private int _currentPort = -1;

    public BazaarAgentRuntimeController(
        IBazaarAgentOptions options,
        IBazaarAgentContextReader contextReader,
        IBazaarAgentActionDispatcher dispatcher,
        IBazaarAgentLogger logger,
        IBazaarAgentClock clock,
        Action? snapshotPublished = null
    )
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _snapshotPublished = snapshotPublished;
    }

    public BazaarAgentContextSnapshot? CurrentSnapshot => _snapshots.Current;

    public bool Tick()
    {
        ReconcileListener();
        if (_http is null || _queue is null)
            return false;

        if (_clock.NowSeconds - _lastTickTime < SnapshotPublishIntervalSeconds)
            return false;
        _lastTickTime = _clock.NowSeconds;

        var cooldownLeft = ComputeCooldownLeft();
        var context = _contextReader.Build(cooldownLeft);
        var snapshot = _snapshots.Publish(context);
        if (snapshot.TickId == 1)
            _logger.Info($"First snapshot published. state={context.StateName}");

        _snapshotPublished?.Invoke();

        var pending = _queue.TryDequeue();
        if (pending is not null)
            ProcessPending(pending, snapshot);

        return true;
    }

    private void ReconcileListener()
    {
        if (_clock.NowSeconds - _lastListenerReconcileTime < ListenerReconcileDebounceSeconds)
            return;
        _lastListenerReconcileTime = _clock.NowSeconds;

        var desiredPort = BazaarAgentRuntimeDefaults.HttpListenerPort;
        var desiredTimeoutMs = BazaarAgentRuntimeDefaults.ActionTimeoutMilliseconds;

        if (_http is not null && desiredPort == _currentPort)
            return;

        if (_http is not null)
        {
            _logger.Info($"Restarting listener (port {_currentPort} -> {desiredPort})");
            StopListener();
        }

        try
        {
            _queue = new BazaarAgentActionQueue(desiredTimeoutMs);
            _http = new BazaarAgentHttpServer(
                desiredPort,
                () => _snapshots.Current,
                _queue,
                _logger
            );
            _http.Start();
            _currentPort = desiredPort;
            _logger.Info($"Listener started on http://127.0.0.1:{desiredPort}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Listener failed on port {desiredPort}", ex);
            StopListener();
        }
    }

    private void StopListener()
    {
        try
        {
            _http?.Stop();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Listener stop failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            _queue?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Listener queue disposal failed: {ex.GetType().Name}: {ex.Message}");
        }

        _http = null;
        _queue = null;
        _currentPort = -1;
    }

    private void ProcessPending(PendingAction pending, BazaarAgentContextSnapshot snapshot)
    {
        var action = pending.Action;
        var decisionId = BazaarAgentUlid.New();
        var cooldownLeft = ComputeCooldownLeft();

        var validation = BazaarAgentActionValidator.Validate(snapshot, action, cooldownLeft);
        if (validation.Code != BazaarAgentValidationCode.Ok)
        {
            var errorBody = BazaarAgentResponseJson.BuildValidationErrorBody(validation);
            pending.SetResponse(new BazaarAgentServerResponse(validation.HttpStatus, errorBody));
            LogDecision(
                decisionId,
                snapshot,
                action,
                executed: false,
                error: validation.Code.ToString()
            );
            return;
        }

        var result = _dispatcher.Execute(action, snapshot);
        if (result.Executed)
        {
            if (action.ActionKind != BazaarAgentActionKind.Wait)
                _lastActionTime = _clock.NowSeconds;

            var okBody = BuildOkBody(decisionId, snapshot, action, executed: true);
            pending.SetResponse(new BazaarAgentServerResponse(200, okBody));
            LogDecision(decisionId, snapshot, action, executed: true, error: null);
            return;
        }

        var dispatchErrorBody = BuildDispatchErrorBody(result.Error);
        pending.SetResponse(new BazaarAgentServerResponse(500, dispatchErrorBody));
        LogDecision(decisionId, snapshot, action, executed: false, error: result.Error);
    }

    private string BuildDispatchErrorBody(string? details)
    {
        var envelope = new Dictionary<string, object?> { ["error"] = "internal" };
        if (details is not null)
            envelope["details"] = details;
        return JsonConvert.SerializeObject(envelope, _responseJson);
    }

    private string BuildOkBody(
        string decisionId,
        BazaarAgentContextSnapshot snapshot,
        BazaarAgentAction action,
        bool executed
    )
    {
        var payload = new
        {
            schemaVersion = BazaarAgentSchema.Version,
            decisionId,
            executed,
            tickId = snapshot.TickId,
            actionKind = action.ActionKind.ToString(),
        };
        return JsonConvert.SerializeObject(payload, _responseJson);
    }

    private void LogDecision(
        string decisionId,
        BazaarAgentContextSnapshot snapshot,
        BazaarAgentAction action,
        bool executed,
        string? error
    )
    {
        try
        {
            var log = GetOrCreateDecisionLog();
            log.Append(
                new BazaarAgentDecisionLogEntry
                {
                    Ts = _clock.UtcNowIsoString(),
                    TickId = snapshot.TickId,
                    DecisionId = decisionId,
                    RunId = snapshot.Context.RunId,
                    State = snapshot.Context.StateName.ToString(),
                    Action = action,
                    Executed = executed,
                    Error = error,
                    Reason = action.Reason,
                }
            );
        }
        catch (Exception ex)
        {
            _logger.Error("decision log append failed", ex);
        }
    }

    private double ComputeCooldownLeft()
    {
        var elapsed = _clock.NowSeconds - _lastActionTime;
        var remaining = BazaarAgentRuntimeDefaults.ActionMinDelay.TotalSeconds - elapsed;
        return remaining > 0 ? remaining : 0;
    }

    private BazaarAgentDecisionLog GetOrCreateDecisionLog() =>
        _decisionLog ??= new BazaarAgentDecisionLog(_options.DecisionLogRoot);

    public void Dispose()
    {
        StopListener();
    }
}
