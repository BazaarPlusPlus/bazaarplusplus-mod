#nullable enable
namespace BazaarPlusPlus.Game.CombatLog;

internal enum CombatLogPlaybackPass
{
    FirstPlay,
    Replay,
}

internal enum CombatLogRowVisualState
{
    Played,
    Current,
    FutureHidden,
    FutureDimmed,
}

internal static class CombatLogPlaybackState
{
    internal static int GetLastProcessedFrameIndex(int processedFrameCount)
    {
        return processedFrameCount <= 0 ? -1 : processedFrameCount - 1;
    }

    internal static bool TryGetCurrentFrameIndex(int processedFrameCount, out int frameIndex)
    {
        frameIndex = GetLastProcessedFrameIndex(processedFrameCount);
        return frameIndex >= 0;
    }

    internal static CombatLogRowVisualState GetVisualState(
        int rowFrameIndex,
        int processedFrameCount,
        CombatLogPlaybackPass playbackPass
    )
    {
        var lastProcessedFrameIndex = GetLastProcessedFrameIndex(processedFrameCount);
        if (rowFrameIndex < lastProcessedFrameIndex)
            return CombatLogRowVisualState.Played;

        if (rowFrameIndex == lastProcessedFrameIndex)
            return CombatLogRowVisualState.Current;

        return playbackPass == CombatLogPlaybackPass.Replay
            ? CombatLogRowVisualState.FutureDimmed
            : CombatLogRowVisualState.FutureHidden;
    }
}
