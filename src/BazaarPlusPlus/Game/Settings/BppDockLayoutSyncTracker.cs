#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppDockLayoutSyncTracker
{
    private static readonly float[] DelayedProbeSeconds = [0.25f, 0.5f, 1f, 2f, 4f, 8f];
    private const float SteadyStateProbeIntervalSeconds = 2f;

    private readonly int _immediateSyncFrameCount;
    private int _lastSceneHandle = int.MinValue;
    private BppSettingsDockSceneKind _lastSceneKind;
    private int _pendingImmediateSyncFrames;
    private int _nextDelayedProbe;
    private float _sceneChangedAtSeconds;
    private float _nextSteadyStateProbeAtSeconds;

    internal BppDockLayoutSyncTracker(int immediateSyncFrameCount)
    {
        _immediateSyncFrameCount = immediateSyncFrameCount > 0 ? immediateSyncFrameCount : 1;
    }

    internal bool ShouldSync(
        int sceneHandle,
        BppSettingsDockSceneKind sceneKind,
        float realtimeSeconds
    )
    {
        if (sceneHandle != _lastSceneHandle || sceneKind != _lastSceneKind)
        {
            _lastSceneHandle = sceneHandle;
            _lastSceneKind = sceneKind;
            _pendingImmediateSyncFrames = _immediateSyncFrameCount;
            _nextDelayedProbe = 0;
            _sceneChangedAtSeconds = realtimeSeconds;
            _nextSteadyStateProbeAtSeconds =
                realtimeSeconds + DelayedProbeSeconds[^1] + SteadyStateProbeIntervalSeconds;
        }

        if (_pendingImmediateSyncFrames > 0)
        {
            _pendingImmediateSyncFrames--;
            return true;
        }

        var shouldProbe = false;
        while (
            _nextDelayedProbe < DelayedProbeSeconds.Length
            && realtimeSeconds >= _sceneChangedAtSeconds + DelayedProbeSeconds[_nextDelayedProbe]
        )
        {
            _nextDelayedProbe++;
            shouldProbe = true;
        }

        if (
            _nextDelayedProbe >= DelayedProbeSeconds.Length
            && realtimeSeconds >= _nextSteadyStateProbeAtSeconds
        )
        {
            do _nextSteadyStateProbeAtSeconds += SteadyStateProbeIntervalSeconds;
            while (realtimeSeconds >= _nextSteadyStateProbeAtSeconds);

            shouldProbe = true;
        }

        return shouldProbe;
    }
}
