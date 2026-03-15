using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class RunStateSyncController : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.25f;

    private float _nextRefreshAt;

    private void OnEnable()
    {
        ModState.RefreshRunStateFromCurrentState();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
        ModState.RefreshRunStateFromCurrentState();
        Game.RunLogging.RunLoggingController.Instance?.PollRunState();
    }
}
