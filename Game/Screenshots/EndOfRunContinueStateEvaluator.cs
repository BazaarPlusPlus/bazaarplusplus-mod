#nullable enable
using System.Reflection;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class EndOfRunContinueStateEvaluator
{
    private const string TransitionCountFieldName = "_transitionCount";
    private static bool _warnedMissingTransitionField;

    public static bool ShouldAllowContinue(
        object? screenController,
        bool suppressWhileCaptureInFlight
    )
    {
        return TryShouldAllowContinue(
            screenController,
            suppressWhileCaptureInFlight,
            out var shouldAllowContinue
        )
            ? shouldAllowContinue
            : true;
    }

    public static bool TryShouldAllowContinue(
        object? screenController,
        bool suppressWhileCaptureInFlight,
        out bool shouldAllowContinue
    )
    {
        if (suppressWhileCaptureInFlight)
        {
            shouldAllowContinue = false;
            return true;
        }

        if (!TryIsInteractionBlocked(screenController, out var isInteractionBlocked))
        {
            shouldAllowContinue = true;
            return false;
        }

        shouldAllowContinue = !isInteractionBlocked;
        return true;
    }

    public static bool TryIsInteractionBlocked(object? screenController, out bool isInteractionBlocked)
    {
        isInteractionBlocked = false;
        if (screenController == null)
            return false;

        var transitionCountField = screenController.GetType().GetField(
            TransitionCountFieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        if (transitionCountField == null)
        {
            WarnMissingTransitionFieldOnce();
            return false;
        }

        var transitionCount = (int?)transitionCountField.GetValue(screenController) ?? 0;
        isInteractionBlocked =
            transitionCount > 0 || EndOfRunSummaryRevealDetector.IsSummaryRevealInProgress(screenController);
        return true;
    }

    private static void WarnMissingTransitionFieldOnce()
    {
        if (_warnedMissingTransitionField)
            return;

        _warnedMissingTransitionField = true;
        BppLog.Warn(
            "EndOfRunScreenshot",
            "Failed to resolve EndOfRunScreenController._transitionCount; end-of-run mouse blocking will fall back to the game's default behavior."
        );
    }
}
