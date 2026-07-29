#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

internal enum RecordedReplayPresentationGateKind
{
    None,
    CurrentNativeRecording,
    ManagedSavedRecording,
}

internal static class RecordedReplayPresentationGatePolicy
{
    internal static RecordedReplayPresentationGateKind Resolve(
        bool currentNativeReplayStarted,
        bool savedReplayPlaybackActive,
        bool managedReplayRecordsVideo
    )
    {
        if (currentNativeReplayStarted)
            return RecordedReplayPresentationGateKind.CurrentNativeRecording;

        return savedReplayPlaybackActive && managedReplayRecordsVideo
            ? RecordedReplayPresentationGateKind.ManagedSavedRecording
            : RecordedReplayPresentationGateKind.None;
    }
}
