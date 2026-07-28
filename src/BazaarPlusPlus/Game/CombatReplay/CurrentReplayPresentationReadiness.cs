#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

internal readonly record struct CurrentReplayPresentationReadinessSnapshot(
    bool ReplayActive,
    bool BoardUpdating,
    bool StorageMoving,
    bool HasCardsToReveal,
    int ExpectedItemCount,
    int VisibleItemCount,
    int FaceUpItemCount,
    int SettledItemCount
);

internal static class CurrentReplayPresentationReadiness
{
    internal const int RequiredStableFrames = 2;
    internal const float TimeoutSeconds = 10f;

    internal static bool IsReady(CurrentReplayPresentationReadinessSnapshot snapshot) =>
        snapshot.ReplayActive
        && !snapshot.BoardUpdating
        && !snapshot.StorageMoving
        && !snapshot.HasCardsToReveal
        && snapshot.ExpectedItemCount >= 0
        && snapshot.VisibleItemCount == snapshot.ExpectedItemCount
        && snapshot.FaceUpItemCount == snapshot.ExpectedItemCount
        && snapshot.SettledItemCount == snapshot.ExpectedItemCount;

    internal static int AdvanceStableFrameCount(
        int previousStableFrameCount,
        CurrentReplayPresentationReadinessSnapshot snapshot
    ) => IsReady(snapshot) ? Math.Min(previousStableFrameCount + 1, RequiredStableFrames) : 0;
}
