#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Screenshots;

internal enum EndOfRunCaptureReadinessState
{
    UnknownTarget,
    NotSummary,
    TransitionInProgress,
    RevealNotStarted,
    RevealInProgress,
    Ready,
    DetectionFailed,
}

internal static class EndOfRunCaptureReadinessDetector
{
    private const string TransitionCountFieldName = "_transitionCount";
    private static bool _warnedMissingTransitionCount;
    private static bool _warnedReadinessFailure;

    public static EndOfRunCaptureReadinessState GetState(
        object? screenController,
        bool hasSummaryRevealStarted
    )
    {
        if (screenController == null)
            return EndOfRunCaptureReadinessState.NotSummary;

        try
        {
            var revealState = EndOfRunSummaryRevealDetector.GetRevealState(screenController);
            if (revealState == EndOfRunSummaryRevealState.TargetDetectionFailed)
                return EndOfRunCaptureReadinessState.UnknownTarget;
            if (revealState == EndOfRunSummaryRevealState.NotSummary)
                return EndOfRunCaptureReadinessState.NotSummary;

            if (!TryGetTransitionCount(screenController, out var transitionCount))
            {
                return hasSummaryRevealStarted
                    ? EndOfRunCaptureReadinessState.DetectionFailed
                    : EndOfRunCaptureReadinessState.RevealNotStarted;
            }

            // The native controller keeps this positive throughout preload, the intro/trophy
            // transition, and page transitions. This also prevents an all-null preloaded card
            // array from being mistaken for a genuinely empty, settled board.
            if (transitionCount > 0)
                return EndOfRunCaptureReadinessState.TransitionInProgress;
            if (!hasSummaryRevealStarted)
                return EndOfRunCaptureReadinessState.RevealNotStarted;

            return revealState switch
            {
                EndOfRunSummaryRevealState.NoLoadedCards => EndOfRunCaptureReadinessState.Ready,
                EndOfRunSummaryRevealState.RevealInProgress =>
                    EndOfRunCaptureReadinessState.RevealInProgress,
                EndOfRunSummaryRevealState.RevealComplete => EndOfRunCaptureReadinessState.Ready,
                EndOfRunSummaryRevealState.DetectionFailed =>
                    EndOfRunCaptureReadinessState.DetectionFailed,
                _ => EndOfRunCaptureReadinessState.DetectionFailed,
            };
        }
        catch (Exception ex)
        {
            if (!_warnedReadinessFailure)
            {
                _warnedReadinessFailure = true;
                BppLog.Warn(
                    "EndOfRunScreenshot",
                    $"End-of-run capture readiness detection failed: {ex.Message}"
                );
            }

            return hasSummaryRevealStarted
                ? EndOfRunCaptureReadinessState.DetectionFailed
                : EndOfRunCaptureReadinessState.RevealNotStarted;
        }
    }

    private static bool TryGetTransitionCount(object screenController, out int transitionCount)
    {
        transitionCount = 0;
        var field = screenController
            .GetType()
            .GetField(
                TransitionCountFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (field == null)
        {
            if (!_warnedMissingTransitionCount)
            {
                _warnedMissingTransitionCount = true;
                BppLog.Warn(
                    "EndOfRunScreenshot",
                    "Failed to resolve EndOfRunScreenController._transitionCount; automatic capture will use the bounded fallback."
                );
            }

            return false;
        }

        transitionCount = (int?)field.GetValue(screenController) ?? 0;
        return true;
    }
}
