#nullable enable

namespace BazaarPlusPlus.Game.Screenshots;

internal readonly record struct EndOfRunNativeContinueState(
    int ScreenId,
    bool ScreenStateAvailable,
    bool ScreenActive,
    bool EndOfRunStateAvailable,
    bool IsEndOfRun,
    bool SceneTransitionAvailable,
    bool SceneTransitioning,
    bool TransitionCountAvailable,
    int TransitionCount,
    bool ActiveControllerAvailable,
    int ActiveControllerId,
    bool ActiveControllerActive
);

internal static class EndOfRunNativeContinueVerifier
{
    internal static bool IsTargetCurrent(
        EndOfRunNativeContinueState state,
        EndOfRunContinueTarget target
    ) =>
        state.ScreenId == target.ScreenId
        && state.ScreenStateAvailable
        && state.ScreenActive
        && state.EndOfRunStateAvailable
        && state.IsEndOfRun
        && state.SceneTransitionAvailable
        && !state.SceneTransitioning
        && state.TransitionCountAvailable
        && state.TransitionCount == 0
        && state.ActiveControllerAvailable
        && state.ActiveControllerId == target.SummaryId
        && state.ActiveControllerActive;

    internal static bool HasAdvanced(
        EndOfRunNativeContinueState state,
        EndOfRunContinueTarget target
    )
    {
        if (state.ScreenStateAvailable && !state.ScreenActive)
            return true;
        if (state.EndOfRunStateAvailable && !state.IsEndOfRun)
            return true;
        if (state.SceneTransitionAvailable && state.SceneTransitioning)
            return true;
        if (state.TransitionCountAvailable && state.TransitionCount > 0)
            return true;
        return state.ActiveControllerAvailable
            && (state.ActiveControllerId != target.SummaryId || !state.ActiveControllerActive);
    }
}
