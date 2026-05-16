#nullable enable
using UnityEngine;
using BazaarPlusPlus;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal sealed class AutoBazaarRuntime : MonoBehaviour
{
    private IBppServices? _services;
    private float _lastTickTime;

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

        // Phase 2+ fills this in.
    }

    private void OnDestroy()
    {
        // Phase 4 will stop listener here.
    }
}
