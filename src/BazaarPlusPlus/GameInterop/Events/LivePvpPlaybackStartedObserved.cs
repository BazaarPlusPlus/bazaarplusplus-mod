#nullable enable

namespace BazaarPlusPlus.GameInterop.Events;

internal sealed class LivePvpPlaybackStartedObserved
{
    internal static LivePvpPlaybackStartedObserved Instance { get; } = new();

    private LivePvpPlaybackStartedObserved() { }
}
