#nullable enable
using System;
using System.Reflection;

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

internal readonly record struct EndOfRunCaptureReadinessOutcome(
    EndOfRunCaptureReadinessState State,
    ScreenshotCaptureReasonCode? ReasonCode,
    Exception? Exception
);

internal static class EndOfRunCaptureReadinessDetector
{
    private const string TransitionCountFieldName = "_transitionCount";

    public static EndOfRunCaptureReadinessState GetState(
        object? screenController,
        bool hasSummaryRevealStarted
    ) => GetOutcome(screenController, hasSummaryRevealStarted).State;

    public static EndOfRunCaptureReadinessOutcome GetOutcome(
        object? screenController,
        bool hasSummaryRevealStarted
    )
    {
        if (screenController == null)
            return Outcome(EndOfRunCaptureReadinessState.NotSummary);

        try
        {
            var reveal = EndOfRunSummaryRevealDetector.GetRevealOutcome(screenController);
            var revealState = reveal.State;
            if (revealState == EndOfRunSummaryRevealState.TargetDetectionFailed)
                return Outcome(
                    EndOfRunCaptureReadinessState.UnknownTarget,
                    reveal.ReasonCode,
                    reveal.Exception
                );
            if (revealState == EndOfRunSummaryRevealState.NotSummary)
                return Outcome(EndOfRunCaptureReadinessState.NotSummary);

            if (!TryGetTransitionCount(screenController, out var transitionCount))
            {
                return Outcome(
                    hasSummaryRevealStarted
                        ? EndOfRunCaptureReadinessState.DetectionFailed
                        : EndOfRunCaptureReadinessState.RevealNotStarted,
                    ScreenshotCaptureReasonCode.TransitionFieldMissing
                );
            }

            // The native controller keeps this positive throughout preload, the intro/trophy
            // transition, and page transitions. This also prevents an all-null preloaded card
            // array from being mistaken for a genuinely empty, settled board.
            if (transitionCount > 0)
                return Outcome(EndOfRunCaptureReadinessState.TransitionInProgress);
            if (!hasSummaryRevealStarted)
                return Outcome(EndOfRunCaptureReadinessState.RevealNotStarted);

            var state = revealState switch
            {
                EndOfRunSummaryRevealState.NoLoadedCards => EndOfRunCaptureReadinessState.Ready,
                EndOfRunSummaryRevealState.RevealInProgress =>
                    EndOfRunCaptureReadinessState.RevealInProgress,
                EndOfRunSummaryRevealState.RevealComplete => EndOfRunCaptureReadinessState.Ready,
                EndOfRunSummaryRevealState.DetectionFailed =>
                    EndOfRunCaptureReadinessState.DetectionFailed,
                _ => EndOfRunCaptureReadinessState.DetectionFailed,
            };
            return Outcome(
                state,
                state == EndOfRunCaptureReadinessState.DetectionFailed
                    ? reveal.ReasonCode ?? ScreenshotCaptureReasonCode.RevealProbeFailed
                    : null,
                reveal.Exception
            );
        }
        catch (Exception ex)
        {
            return Outcome(
                hasSummaryRevealStarted
                    ? EndOfRunCaptureReadinessState.DetectionFailed
                    : EndOfRunCaptureReadinessState.RevealNotStarted,
                ScreenshotCaptureReasonCode.RevealProbeFailed,
                ex
            );
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
            return false;

        transitionCount = (int?)field.GetValue(screenController) ?? 0;
        return true;
    }

    private static EndOfRunCaptureReadinessOutcome Outcome(
        EndOfRunCaptureReadinessState state,
        ScreenshotCaptureReasonCode? reasonCode = null,
        Exception? exception = null
    ) => new(state, reasonCode, exception);
}
