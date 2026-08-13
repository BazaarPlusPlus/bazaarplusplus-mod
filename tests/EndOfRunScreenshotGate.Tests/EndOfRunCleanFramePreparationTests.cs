#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.GameInterop.Tooltips;

internal static class EndOfRunCleanFramePreparationTests
{
    internal static void Run()
    {
        Capture_waits_for_a_clean_post_cleanup_pose_window();
        Pose_change_restarts_the_post_cleanup_window();
        Late_tooltip_visibility_invalidates_the_clean_window();
        Empty_board_can_capture_after_a_clean_render_boundary();
        Unavailable_native_suppression_fails_clean();
        Native_suppression_owners_are_reference_counted_independently();
    }

    private static void Capture_waits_for_a_clean_post_cleanup_pose_window()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 10f);

        Assert(
            core.Observe(Clean(), Sample(2, 10, 100), nowSeconds: 10f).Kind
                == EndOfRunCleanFrameDecisionKind.Wait,
            "The pre-suppression readiness verdict must not authorize capture."
        );
        Assert(
            core.Observe(Clean(), Sample(2, 10, 100), nowSeconds: 10.49f).Kind
                == EndOfRunCleanFrameDecisionKind.Wait,
            "Capture must wait for the full post-clean pose window."
        );
        Assert(
            core.Observe(Clean(), Sample(2, 10, 100), nowSeconds: 10.5f).Kind
                == EndOfRunCleanFrameDecisionKind.Capture,
            "A clean tooltip audit plus a newly stable card pose should authorize capture."
        );
    }

    private static void Pose_change_restarts_the_post_cleanup_window()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        _ = core.Observe(Clean(), Sample(1, 11, 100), 0f);
        _ = core.Observe(Clean(), Sample(1, 11, 101), 0.4f);

        Assert(
            core.Observe(Clean(), Sample(1, 11, 101), 0.89f).Kind
                == EndOfRunCleanFrameDecisionKind.Wait,
            "Tooltip cleanup card movement must invalidate the old readiness verdict."
        );
        Assert(
            core.Observe(Clean(), Sample(1, 11, 101), 0.91f).Kind
                == EndOfRunCleanFrameDecisionKind.Capture,
            "Capture should resume only after the changed pose settles again."
        );
    }

    private static void Late_tooltip_visibility_invalidates_the_clean_window()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        _ = core.Observe(Clean(), Sample(1, 12, 200), 0f);
        _ = core.Observe(
            new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Dirty),
            Sample(1, 12, 200),
            0.4f
        );

        Assert(
            core.Observe(Clean(), Sample(1, 12, 200), 0.8f).Kind
                == EndOfRunCleanFrameDecisionKind.Wait,
            "A late fade or show must reset the clean window."
        );
        Assert(
            core.Observe(Clean(), Sample(1, 12, 200), 1.31f).Kind
                == EndOfRunCleanFrameDecisionKind.Capture,
            "The final clean audit must own a complete fresh pose window."
        );
    }

    private static void Empty_board_can_capture_after_a_clean_render_boundary()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 2f);
        var decision = core.Observe(
            Clean(),
            EndOfRunCleanFrameVisualObservation.Empty,
            nowSeconds: 2f
        );

        Assert(
            decision.Kind == EndOfRunCleanFrameDecisionKind.Capture,
            "A clean empty summary should not wait for a nonexistent card pose."
        );

        var expired = core.Observe(
            Clean(),
            EndOfRunCleanFrameVisualObservation.Empty,
            nowSeconds: 7f
        );
        Assert(
            expired.Kind == EndOfRunCleanFrameDecisionKind.Fail
                && expired.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameDeadline,
            "An empty summary must not bypass the bounded clean-frame deadline."
        );
    }

    private static void Unavailable_native_suppression_fails_clean()
    {
        var unavailable = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f).Observe(
            new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Unavailable),
            Sample(1, 1, 1),
            nowSeconds: 0f
        );
        Assert(
            unavailable.Kind == EndOfRunCleanFrameDecisionKind.Fail
                && unavailable.ReasonCode
                    == ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable,
            "A missing native seam must fail clean before ScreenCapture."
        );

        var deadlineCore = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        var deadline = deadlineCore.Observe(
            new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Dirty),
            Sample(1, 1, 1),
            EndOfRunCleanFramePreparationCore.DeadlineSeconds
        );
        Assert(
            deadline.Kind == EndOfRunCleanFrameDecisionKind.Fail
                && deadline.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameDeadline,
            "A persistently dirty frame must stop at the bounded clean deadline."
        );
    }

    private static void Native_suppression_owners_are_reference_counted_independently()
    {
        var ownership = new NativeTooltipSuppressionOwnershipCore();
        ownership.Acquire(NativeTooltipSuppressionOwner.ReplayPresentation);
        ownership.Acquire(NativeTooltipSuppressionOwner.ReplayVideoRecording);
        ownership.Acquire(NativeTooltipSuppressionOwner.EndOfRunCapture);
        ownership.Acquire(NativeTooltipSuppressionOwner.EndOfRunCapture);

        ownership.Release(NativeTooltipSuppressionOwner.ReplayPresentation);
        ownership.Release(NativeTooltipSuppressionOwner.EndOfRunCapture);
        Assert(ownership.IsActive, "One owner must not release another owner's suppression.");
        Assert(
            ownership.LeaseCount(NativeTooltipSuppressionOwner.EndOfRunCapture) == 1,
            "Nested leases for one owner must remain reference counted."
        );

        ownership.Release(NativeTooltipSuppressionOwner.ReplayVideoRecording);
        ownership.Release(NativeTooltipSuppressionOwner.EndOfRunCapture);
        ownership.Release(NativeTooltipSuppressionOwner.EndOfRunCapture);
        Assert(!ownership.IsActive, "Only the final valid lease release should deactivate gates.");
    }

    private static NativeTooltipCleanFrameAudit Clean() => new(NativeTooltipCleanFrameState.Clean);

    private static EndOfRunCleanFrameVisualObservation Sample(
        int loadedCardCount,
        ulong cardSetFingerprint,
        ulong poseFingerprint
    ) =>
        EndOfRunCleanFrameVisualObservation.Sampled(
            loadedCardCount,
            cardSetFingerprint,
            poseFingerprint
        );

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
