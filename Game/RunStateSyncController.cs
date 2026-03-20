using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class RunStateSyncController : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.25f;

    private float _nextRefreshAt;

    private void OnEnable()
    {
        BppRuntimeHost.RunLifecycle.RefreshRunStateFromCurrentState();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
        BppRuntimeHost.RunLifecycle.RefreshRunStateFromCurrentState();
        BppRuntimeHost.EventBus.Publish(new RunLoggingSyncRequested());
    }
}
