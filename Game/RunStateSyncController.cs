#nullable enable
using BazaarPlusPlus.Core.Events;
using System;
using BazaarPlusPlus.Game.RunLifecycle;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunStateSyncController : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.25f;

    private float _nextRefreshAt;
    private IBppEventBus? _eventBus;
    private RunLifecycleModule? _runLifecycle;

    internal void Initialize(IBppEventBus eventBus, RunLifecycleModule runLifecycle)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _runLifecycle = runLifecycle ?? throw new ArgumentNullException(nameof(runLifecycle));

        if (isActiveAndEnabled)
            _runLifecycle.RefreshRunStateFromCurrentState();
    }

    private void OnEnable()
    {
        _runLifecycle?.RefreshRunStateFromCurrentState();
    }

    private void Update()
    {
        if (_eventBus == null || _runLifecycle == null)
            return;

        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
        _runLifecycle.RefreshRunStateFromCurrentState();
        _eventBus.Publish(new RunLoggingSyncRequested());
    }
}
