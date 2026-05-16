#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using BazaarPlusPlus;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal sealed class AutoBazaarRuntime : MonoBehaviour
{
    private IBppServices? _services;
    private float _lastTickTime;
    private readonly AutoBazaarContextSnapshotPublisher _snapshots = new();
    private float _lastActionTime = float.NegativeInfinity;
    private const float ActionMinDelaySeconds = 1.0f;
    private AutoBazaarDecisionLog? _decisionLog;

    private AutoBazaarHttpServer? _http;
    private AutoBazaarActionQueue? _queue;
    private int _currentPort = -1;
    private float _lastListenerReconcileTime = float.NegativeInfinity;
    private const float ListenerReconcileDebounceSeconds = 0.5f;
    private readonly JsonSerializerSettings _responseJson = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
    };

    internal AutoBazaarContextSnapshot? CurrentSnapshot => _snapshots.Current;

    public void Initialize(IBppServices services)
    {
        _services = services;
        BppLog.Info("AutoBazaar", "AutoBazaarRuntime initialized");
    }

    private void Update()
    {
        if (_services is null) return;

        ReconcileListener();
        if (_http is null || _queue is null) return;

        var interval = Mathf.Clamp(_services.Config.AutoBazaarDecisionIntervalSeconds?.Value ?? 1.5f, 0.5f, 10f);
        if (Time.unscaledTime - _lastTickTime < interval) return;
        _lastTickTime = Time.unscaledTime;

        var cooldownLeft = ComputeCooldownLeft();
        var ctx = AutoBazaarContextBuilder.Build(_services, cooldownLeft);
        var snap = _snapshots.Publish(ctx);
        if (snap.TickId == 1)
        {
            BppLog.Info("AutoBazaar", $"First snapshot published. state={ctx.StateName}");
        }
        AutoBazaarUiPlumbing.Tick();

        var pending = _queue.TryDequeue();
        if (pending is null) return;

        ProcessPending(pending, snap);
    }

    private void ReconcileListener()
    {
        if (_services is null) return;
        if (Time.unscaledTime - _lastListenerReconcileTime < ListenerReconcileDebounceSeconds) return;
        _lastListenerReconcileTime = Time.unscaledTime;

        var enabled = _services.Config.AutoBazaarEnabled?.Value == true;
        var desiredPort = _services.Config.AutoBazaarHttpListenerPort?.Value ?? 47900;
        var desiredTimeoutMs = (int)((_services.Config.AutoBazaarHttpEndpointTimeoutSeconds?.Value ?? 3f) * 1000);

        if (!enabled)
        {
            if (_http is not null)
            {
                BppLog.Info("AutoBazaar", "Stopping listener (Enabled=false)");
                try { _http.Stop(); } catch { }
                try { _queue?.Dispose(); } catch { }
                _http = null; _queue = null; _currentPort = -1;
                _snapshots.Reset();  // tickId restarts on next enable
            }
            return;
        }

        // enabled — make sure we're listening on the right port
        if (_http is not null && desiredPort == _currentPort) return;  // already correct

        // Need to (re)start
        if (_http is not null)
        {
            BppLog.Info("AutoBazaar", $"Restarting listener (port {_currentPort} → {desiredPort})");
            try { _http.Stop(); } catch { }
            try { _queue?.Dispose(); } catch { }
            _http = null; _queue = null;
        }

        try
        {
            _queue = new AutoBazaarActionQueue(desiredTimeoutMs);
            var endpointPath = System.IO.Path.Combine(GetLogRoot(), "endpoint.json");
            _http = new AutoBazaarHttpServer(desiredPort, endpointPath, () => _snapshots.Current, _queue);
            _http.Start();
            _currentPort = desiredPort;
            BppLog.Info("AutoBazaar", $"Listener started on http://127.0.0.1:{desiredPort}");
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", $"Listener failed on port {desiredPort}", ex);
            try { _http?.Stop(); } catch { }
            try { _queue?.Dispose(); } catch { }
            _http = null; _queue = null; _currentPort = -1;
        }
    }

    private void ProcessPending(PendingAction pending, AutoBazaarContextSnapshot snap)
    {
        var action = pending.Action;
        var decisionId = AutoBazaarUlid.New();
        var cooldownLeft = ComputeCooldownLeft();

        var validation = AutoBazaarActionValidator.Validate(snap, action, cooldownLeft);
        if (validation.Code != AutoBazaarValidationCode.Ok)
        {
            var errBody = BuildErrorBody(validation);
            pending.SetResponse(new AutoBazaarServerResponse(validation.HttpStatus, errBody));
            LogDecision(decisionId, snap, action, executed: false, error: validation.Code.ToString());
            return;
        }

        var result = AutoBazaarActionDispatcher.Execute(action, snap);

        if (result.Executed)
        {
            if (action.ActionKind != AutoBazaarActionKind.Wait)
            {
                _lastActionTime = Time.unscaledTime;
            }
            var okBody = BuildOkBody(decisionId, snap, action, executed: true);
            pending.SetResponse(new AutoBazaarServerResponse(200, okBody));
            LogDecision(decisionId, snap, action, executed: true, error: null);
            return;
        }

        var dispatchErrBody = BuildDispatchErrorBody(result.Error);
        pending.SetResponse(new AutoBazaarServerResponse(500, dispatchErrBody));
        LogDecision(decisionId, snap, action, executed: false, error: result.Error);
    }

    private string BuildDispatchErrorBody(string? details)
    {
        var envelope = new Dictionary<string, object?> { ["error"] = "internal" };
        if (details is not null) envelope["details"] = details;
        return JsonConvert.SerializeObject(envelope, _responseJson);
    }

    private string BuildOkBody(string decisionId, AutoBazaarContextSnapshot snap, AutoBazaarAction action, bool executed)
    {
        var payload = new
        {
            schemaVersion = "1.0.0",
            decisionId,
            executed,
            tickId = snap.TickId,
            actionKind = action.ActionKind.ToString(),
        };
        return JsonConvert.SerializeObject(payload, _responseJson);
    }

    private string BuildErrorBody(AutoBazaarValidationResult validation)
    {
        var code = validation.Code switch
        {
            AutoBazaarValidationCode.Invalid => "invalid",
            AutoBazaarValidationCode.StaleOrUnavailable => "stale-or-unavailable",
            AutoBazaarValidationCode.Cooldown => "cooldown",
            AutoBazaarValidationCode.Unavailable => "unavailable",
            _ => "internal",
        };
        var envelope = new Dictionary<string, object?>
        {
            ["error"] = code,
        };
        if (validation.Details is not null) envelope["details"] = validation.Details;
        if (validation.Extra is not null)
        {
            foreach (var kv in validation.Extra) envelope[kv.Key] = kv.Value;
        }
        return JsonConvert.SerializeObject(envelope, _responseJson);
    }

    private void LogDecision(string decisionId, AutoBazaarContextSnapshot snap, AutoBazaarAction action, bool executed, string? error)
    {
        try
        {
            var log = GetOrCreateDecisionLog();
            log.Append(new AutoBazaarDecisionLogEntry
            {
                TickId = snap.TickId,
                DecisionId = decisionId,
                RunId = snap.Context.RunId,
                State = snap.Context.StateName.ToString(),
                Action = action,
                Executed = executed,
                Error = error,
                Reason = action.Reason,
            });
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", "decision log append failed", ex);
        }
    }

    private double ComputeCooldownLeft()
    {
        var elapsed = Time.unscaledTime - _lastActionTime;
        var remaining = ActionMinDelaySeconds - elapsed;
        return remaining > 0 ? remaining : 0;
    }

    private AutoBazaarDecisionLog GetOrCreateDecisionLog()
    {
        if (_decisionLog is null)
        {
            var root = GetLogRoot();
            _decisionLog = new AutoBazaarDecisionLog(root);
        }
        return _decisionLog;
    }

    private static string GetLogRoot()
        => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BazaarPlusPlus", "AutoBazaar"));

    private void OnDestroy()
    {
        try { _http?.Stop(); } catch { }
        try { _queue?.Dispose(); } catch { }
        _http = null; _queue = null;
    }
}
