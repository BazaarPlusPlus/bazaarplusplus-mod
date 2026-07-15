#nullable enable

namespace BazaarPlusPlus.GameInterop.Events;

internal sealed class LivePvpPlaybackEndedObserved
{
    internal static LivePvpPlaybackEndedObserved Instance { get; } = new();

    private LivePvpPlaybackEndedObserved() { }
}
