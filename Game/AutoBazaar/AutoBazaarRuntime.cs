#nullable enable
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

    internal AutoBazaarContextSnapshot? CurrentSnapshot => _snapshots.Current;

    public void Initialize(IBppServices services)
    {
        _services = services;
        BppLog.Info("AutoBazaar", "AutoBazaarRuntime initialized");
    }

    private void Update()
    {
        if (_services is null) return;
        if (_services.Config.AutoBazaarEnabled?.Value != true) return;

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
        // Phase 3+ drains the action queue here.
    }

    private double ComputeCooldownLeft()
    {
        var elapsed = Time.unscaledTime - _lastActionTime;
        var remaining = ActionMinDelaySeconds - elapsed;
        return remaining > 0 ? remaining : 0;
    }

    private void OnDestroy()
    {
        // Phase 4 will stop listener here.
    }
}
