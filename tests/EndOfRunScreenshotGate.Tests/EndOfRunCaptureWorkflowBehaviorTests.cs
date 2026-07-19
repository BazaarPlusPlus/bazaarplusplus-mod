#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Infrastructure;
using BepInEx.Logging;

internal static class EndOfRunCaptureWorkflowBehaviorTests
{
    internal static void Run()
    {
        Missing_surface_is_fail_open();
        Native_transition_evidence_requires_positive_proof();
        Ready_waits_for_continue_without_automatic_capture();
        Pre_ready_continue_is_not_latched();
        Detection_failure_still_waits_for_continue();
        Reveal_and_success_flow();
        Duplicate_continue_requests_do_not_duplicate_operations();
        Capture_timeout_starts_when_capture_is_invoked();
        Retryable_failures_get_one_retry_only();
        Adapter_non_retryable_failure_fails_open();
        Reveal_and_capture_timeouts_fail_open_without_retry();
        Metadata_failure_and_timeout_keep_the_verified_artifact();
        Unexpected_failure_after_capture_preserves_the_verified_artifact();
        Failed_unblock_is_retried_without_blocking_continue();
        Context_expiry_cancels_and_cleans_up_a_late_artifact();
        Summary_replacement_discards_stale_continuation();
        Controller_replacement_discards_stale_continuation();
        Stale_click_before_frame_is_consumed_once();
        Reinitialization_discards_deferred_continuation();
        Competing_active_controller_rejects_native_continuation();
        Continue_target_probe_failure_allows_manual_retry();
        Ambiguous_controller_context_loss_is_terminal();
        End_of_run_exit_discards_stale_continuation();
        Reset_cancels_the_old_generation_and_allows_a_new_run();
        Native_continue_no_op_is_diagnostic_and_fail_open();
        Disabled_and_non_summary_continue_are_fail_open();
        Terminal_outcome_is_emitted_once();
    }

    private static void Ready_waits_for_continue_without_automatic_capture()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);

        for (var frame = 0; frame < 10; frame++)
            fixture.Core.OnFrame(fixture.Context);

        Assert(
            fixture.Surface.BeginCount == 0,
            "A settled summary must wait for a user Continue request without capturing."
        );
        Assert(!fixture.Surface.IsBlocked, "Continue must be available once the summary is ready.");
    }

    private static void Native_transition_evidence_requires_positive_proof()
    {
        var target = new EndOfRunContinueTarget(ScreenId: 7, SummaryId: 70);
        var settled = new EndOfRunNativeContinueState(
            ScreenId: 7,
            ScreenStateAvailable: true,
            ScreenActive: true,
            EndOfRunStateAvailable: true,
            IsEndOfRun: true,
            SceneTransitionAvailable: true,
            SceneTransitioning: false,
            TransitionCountAvailable: true,
            TransitionCount: 0,
            ActiveControllerAvailable: true,
            ActiveControllerId: 70,
            ActiveControllerActive: true
        );

        Assert(
            EndOfRunNativeContinueVerifier.IsTargetCurrent(settled, target),
            "A settled original summary should be current."
        );
        Assert(
            !EndOfRunNativeContinueVerifier.HasAdvanced(settled, target),
            "An unchanged summary must not count as native progress."
        );
        Assert(
            !EndOfRunNativeContinueVerifier.HasAdvanced(
                settled with
                {
                    ScreenStateAvailable = false,
                },
                target
            ),
            "A screen-state probe failure must not count as progress."
        );
        Assert(
            !EndOfRunNativeContinueVerifier.HasAdvanced(
                settled with
                {
                    ActiveControllerAvailable = false,
                },
                target
            ),
            "An active-controller probe failure must not count as progress."
        );
        Assert(
            !EndOfRunNativeContinueVerifier.HasAdvanced(
                settled with
                {
                    TransitionCountAvailable = false,
                },
                target
            ),
            "A transition-count probe failure must not count as progress."
        );
        Assert(
            EndOfRunNativeContinueVerifier.HasAdvanced(
                settled with
                {
                    ActiveControllerId = 71,
                },
                target
            ),
            "A changed active controller should prove page progress."
        );
        Assert(
            EndOfRunNativeContinueVerifier.HasAdvanced(
                settled with
                {
                    SceneTransitioning = true,
                },
                target
            ),
            "A scene transition should prove native progress."
        );
    }

    private static void Pre_ready_continue_is_not_latched()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.RevealInProgress);

        Assert(
            fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "A pre-ready Continue request should be blocked."
        );
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        for (var frame = 0; frame < 5; frame++)
            fixture.Core.OnFrame(fixture.Context);

        Assert(fixture.Surface.BeginCount == 0, "A pre-ready request must not be latched.");
        Assert(!fixture.Surface.IsBlocked, "Readiness should make Continue available again.");

        Assert(
            fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "The user must be able to request Continue again after readiness."
        );
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Only the post-ready request should capture.");
    }

    private static void Detection_failure_still_waits_for_continue()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = new EndOfRunCaptureReadinessOutcome(
            EndOfRunCaptureReadinessState.DetectionFailed,
            ScreenshotCaptureReasonCode.RevealProbeFailed,
            new InvalidOperationException("probe failed")
        );

        fixture.Core.OnFrame(fixture.Context);
        fixture.Clock.Advance(20f);
        fixture.Core.OnFrame(fixture.Context);

        Assert(
            fixture.Surface.BeginCount == 0,
            "Degraded readiness must not retain readiness-triggered capture as a fallback."
        );
        Assert(!fixture.Surface.IsBlocked, "Degraded readiness should make Continue available.");
        fixture.Core.RequestContinue(fixture.Screen, fixture.Context);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "A post-deadline Continue should trigger capture.");
    }

    private static void Duplicate_continue_requests_do_not_duplicate_operations()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Core.OnFrame(fixture.Context);

        Assert(
            fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "Continue should defer."
        );
        Assert(
            fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "A duplicate request before capture should remain consumed."
        );
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Duplicate requests must create one capture.");

        Assert(
            fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "A duplicate request while capturing should remain consumed."
        );
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Capturing must not create a second operation.");

        fixture.Files.MarkUsable("duplicate.png");
        fixture.Surface.LastAttempt!.Complete(Success("duplicate.png"));
        fixture.Core.OnFrame(fixture.Context);
        fixture.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());
        Assert(
            fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "A duplicate request at metadata completion should remain consumed."
        );
        Assert(fixture.Surface.ResumeCount == 0, "Input intent must not drive terminal progress.");
        Assert(fixture.Surface.BeginCount == 1, "Persisting must not create a second operation.");

        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.ResumeCount == 1, "Duplicate requests must still resume once.");
    }

    private static void Capture_timeout_starts_when_capture_is_invoked()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Surface.NextAttemptHasStarted = false;
        fixture.Core.OnFrame(fixture.Context);
        fixture.Core.RequestContinue(fixture.Screen, fixture.Context);
        fixture.Core.OnFrame(fixture.Context);
        var attempt = fixture.Surface.LastAttempt!;

        fixture.Clock.Advance(20f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(
            attempt.CancelCount == 0 && fixture.Surface.IsBlocked,
            "Next-loop and end-of-frame scheduling must not consume the capture timeout."
        );

        attempt.HasCaptureStarted = true;
        fixture.Core.OnFrame(fixture.Context);
        fixture.Clock.Advance(14.99f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(attempt.CancelCount == 0, "Capture should retain the full 15-second timeout.");
        fixture.Clock.Advance(0.02f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(attempt.CancelCount == 1, "Capture should time out after its own deadline.");
    }

    private static void Missing_surface_is_fail_open()
    {
        var core = new EndOfRunCaptureWorkflowCore<FakeScreen>(
            new FakePersistence(),
            new FakeClock(),
            new FakeFileSystem(),
            EndOfRunCapturePolicy.Default
        );
        var screen = new FakeScreen { Id = 1 };

        Assert(
            !core.RequestContinue(screen, new EndOfRunCaptureContext(true, "run", "hero")),
            "Patches must fail open before the runtime surface is mounted."
        );
    }

    private static void Reveal_and_success_flow()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.RevealNotStarted);

        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 0, "Capture must wait for native reveal start.");
        Assert(
            fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "Continue should remain blocked while reveal has not started."
        );

        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 0, "Ready reveal must wait for Continue.");
        Assert(!fixture.Surface.IsBlocked, "Ready reveal should make Continue available.");
        Assert(
            fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "The first ready Continue request should be consumed."
        );
        Assert(fixture.Surface.BeginCount == 0, "Continue should defer capture to the next frame.");
        Assert(fixture.Surface.IsBlocked, "Deferred Continue should immediately block duplicates.");

        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "The next frame should begin exactly one attempt.");

        var attempt = fixture.Surface.LastAttempt!;
        fixture.Files.MarkUsable("success.png");
        attempt.Complete(Success("success.png"));
        fixture.Core.OnFrame(fixture.Context);
        Assert(attempt.RestoreCount == 1, "PNG validation must restore UI before persistence.");
        Assert(fixture.Persistence.CallCount == 1, "Verified artifact should persist metadata.");
        Assert(fixture.Surface.IsBlocked, "Metadata persistence should keep Continue blocked.");

        fixture.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());
        fixture.Core.OnFrame(fixture.Context);
        Assert(!fixture.Surface.IsBlocked, "Successful persistence should release Continue.");
        Assert(fixture.Surface.ResumeCount == 1, "Terminal success should resume Continue once.");
        Assert(
            !fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "Terminal success must remain fail-open."
        );
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Repeated frames must not duplicate capture.");
        Assert(fixture.Surface.ResumeCount == 1, "Repeated frames must not resume Continue twice.");
    }

    private static void Adapter_non_retryable_failure_fails_open()
    {
        var fixture = ReadyFixture();
        fixture.Surface.LastAttempt!.Complete(
            EndOfRunCaptureAttemptOutcome.Failed(ScreenshotCaptureReasonCode.ContextExpired)
        );

        fixture.Core.OnFrame(fixture.Context);
        fixture.Clock.Advance(10f);
        fixture.Core.OnFrame(fixture.Context);

        Assert(!fixture.Surface.IsBlocked, "Context expiry from the adapter must fail open.");
        Assert(fixture.Surface.BeginCount == 1, "Context expiry from the adapter must not retry.");
        Assert(fixture.Surface.LastAttempt.RestoreCount == 1, "Failure must restore UI once.");
        Assert(fixture.Surface.ResumeCount == 1, "Terminal adapter failure should resume once.");
    }

    private static void Retryable_failures_get_one_retry_only()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Surface.ThrowOnNextBegin = new InvalidOperationException("sync capture failure");
        fixture.Core.OnFrame(fixture.Context);
        fixture.Core.RequestContinue(fixture.Screen, fixture.Context);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Synchronous failure should count as attempt one.");
        Assert(fixture.Surface.IsBlocked, "A scheduled retry should keep Continue blocked.");

        fixture.Clock.Advance(0.99f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Retry should respect the cooldown.");
        fixture.Clock.Advance(0.02f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 2, "First failure should retry exactly once.");

        fixture.Surface.LastAttempt!.Complete(
            EndOfRunCaptureAttemptOutcome.Failed(
                ScreenshotCaptureReasonCode.CaptureTaskFaulted,
                new InvalidOperationException("task fault")
            )
        );
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.ResumeCount == 1, "Retry exhaustion should resume once.");
        Assert(!fixture.Surface.IsBlocked, "Second retryable failure must fail open.");
        fixture.Clock.Advance(10f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 2, "Retry budget must be capped at two attempts.");

        foreach (
            var outcome in new[]
            {
                EndOfRunCaptureAttemptOutcome.Failed(
                    ScreenshotCaptureReasonCode.CaptureReturnedNull
                ),
                Success("empty.png"),
            }
        )
        {
            var retryFixture = ReadyFixture();
            retryFixture.Surface.LastAttempt!.Complete(outcome);
            retryFixture.Core.OnFrame(retryFixture.Context);
            retryFixture.Clock.Advance(1f);
            retryFixture.Core.OnFrame(retryFixture.Context);
            Assert(
                retryFixture.Surface.BeginCount == 2,
                "Null and unusable artifacts should each receive one retry."
            );
        }
    }

    private static void Reveal_and_capture_timeouts_fail_open_without_retry()
    {
        var revealFixture = new Fixture();
        revealFixture.Core.OnEndOfRunInitializing();
        revealFixture.Core.ObserveRevealStarted(revealFixture.Screen);
        revealFixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.RevealInProgress);
        revealFixture.Core.OnFrame(revealFixture.Context);
        revealFixture.Clock.Advance(20f);
        revealFixture.Core.OnFrame(revealFixture.Context);
        Assert(
            revealFixture.Surface.BeginCount == 0 && !revealFixture.Surface.IsBlocked,
            "Known in-progress reveal should fail open at its deadline without capture."
        );
        Assert(
            revealFixture.Surface.ResumeCount == 0,
            "A reveal timeout before a valid Continue request must not auto-advance."
        );

        var captureFixture = ReadyFixture();
        var attempt = captureFixture.Surface.LastAttempt!;
        captureFixture.Clock.Advance(15f);
        captureFixture.Core.OnFrame(captureFixture.Context);
        Assert(
            attempt.CancelCount == 1,
            "Capture timeout should cancel the active adapter attempt."
        );
        Assert(!captureFixture.Surface.IsBlocked, "Capture timeout should immediately fail open.");
        Assert(captureFixture.Surface.ResumeCount == 1, "Capture timeout should resume once.");
        captureFixture.Clock.Advance(100f);
        captureFixture.Core.OnFrame(captureFixture.Context);
        Assert(captureFixture.Surface.BeginCount == 1, "Capture timeout must not retry.");

        captureFixture.Files.MarkUsable("late-timeout.png");
        attempt.Complete(Success("late-timeout.png"));
        WaitUntil(
            () => captureFixture.Files.Deleted.Contains("late-timeout.png"),
            "Timed-out late artifact should be deleted."
        );
        captureFixture.Core.OnFrame(captureFixture.Context);
        Assert(
            captureFixture.Surface.ResumeCount == 1,
            "Late capture completion must not resume twice."
        );
    }

    private static void Metadata_failure_and_timeout_keep_the_verified_artifact()
    {
        var failed = ReadyFixture();
        failed.Files.MarkUsable("metadata-failed.png");
        failed.Surface.LastAttempt!.Complete(Success("metadata-failed.png"));
        failed.Core.OnFrame(failed.Context);
        failed.Persistence.CompleteNext(
            ScreenshotMetadataPersistenceOutcome.Failed(
                new InvalidOperationException("metadata failed")
            )
        );
        failed.Core.OnFrame(failed.Context);
        Assert(!failed.Surface.IsBlocked, "Metadata failure should terminate degraded.");
        Assert(failed.Surface.ResumeCount == 1, "Metadata failure should resume once.");
        Assert(failed.Surface.BeginCount == 1, "Metadata failure must not recapture.");
        Assert(
            !failed.Files.Deleted.Contains("metadata-failed.png"),
            "Metadata failure must preserve the verified file."
        );

        var timedOut = ReadyFixture();
        timedOut.Files.MarkUsable("metadata-timeout.png");
        timedOut.Surface.LastAttempt!.Complete(Success("metadata-timeout.png"));
        timedOut.Core.OnFrame(timedOut.Context);
        timedOut.Clock.Advance(5f);
        timedOut.Core.OnFrame(timedOut.Context);
        Assert(!timedOut.Surface.IsBlocked, "Metadata timeout should terminate degraded.");
        Assert(timedOut.Surface.ResumeCount == 1, "Metadata timeout should resume once.");
        Assert(timedOut.Surface.BeginCount == 1, "Metadata timeout must not recapture.");
        Assert(
            !timedOut.Files.Deleted.Contains("metadata-timeout.png"),
            "Metadata timeout must preserve the verified file."
        );
        timedOut.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());
        timedOut.Core.OnFrame(timedOut.Context);
        Assert(
            timedOut.Surface.ResumeCount == 1,
            "Late metadata completion must not resume twice."
        );
    }

    private static void Unexpected_failure_after_capture_preserves_the_verified_artifact()
    {
        var fixture = ReadyFixture();
        fixture.Files.MarkUsable("unexpected-metadata.png");
        fixture.Surface.LastAttempt!.Complete(Success("unexpected-metadata.png"));
        fixture.Core.OnFrame(fixture.Context);

        fixture.Core.FailOpenUnexpected(new InvalidOperationException("unexpected"));

        Assert(!fixture.Surface.IsBlocked, "Unexpected failures must release Continue.");
        Assert(fixture.Surface.ResumeCount == 1, "Unexpected fail-open should resume once.");
        Assert(fixture.Surface.BeginCount == 1, "Unexpected failures must not recapture.");
        Assert(
            !fixture.Files.Deleted.Contains("unexpected-metadata.png"),
            "A verified artifact must survive fail-open during metadata persistence."
        );

        var restoreFailure = ReadyFixture();
        restoreFailure.Files.MarkUsable("unexpected-restore.png");
        restoreFailure.Surface.LastAttempt!.ThrowOnRestore = new InvalidOperationException(
            "restore failed"
        );
        restoreFailure.Surface.LastAttempt.Complete(Success("unexpected-restore.png"));
        restoreFailure.Core.OnFrame(restoreFailure.Context);
        SpinWait.SpinUntil(
            () => restoreFailure.Files.Deleted.Contains("unexpected-restore.png"),
            TimeSpan.FromMilliseconds(100)
        );
        Assert(
            !restoreFailure.Files.Deleted.Contains("unexpected-restore.png"),
            "A verified artifact must survive fail-open when UI restoration throws."
        );
        Assert(restoreFailure.Surface.ResumeCount == 1, "Restore failure should resume once.");
    }

    private static void Failed_unblock_is_retried_without_blocking_continue()
    {
        var fixture = ReadyFixture();
        fixture.Files.MarkUsable("unblock.png");
        fixture.Surface.LastAttempt!.Complete(Success("unblock.png"));
        fixture.Core.OnFrame(fixture.Context);
        fixture.Surface.UnblockFailuresRemaining = 1;
        fixture.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());

        fixture.Core.OnFrame(fixture.Context);
        Assert(
            !fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "A failed physical detach must still fail open logically."
        );
        Assert(!fixture.Surface.IsBlocked, "A later frame should retry physical blocker detach.");
        Assert(fixture.Surface.UnblockCount >= 2, "Failed unblock should be retried.");
    }

    private static void Context_expiry_cancels_and_cleans_up_a_late_artifact()
    {
        var fixture = ReadyFixture();
        var attempt = fixture.Surface.LastAttempt!;
        fixture.Screen.IsActive = false;
        fixture.Surface.ActiveScreen = null;
        fixture.Core.OnFrame(fixture.Context);
        Assert(attempt.CancelCount == 1, "Context expiry should cancel the active attempt.");
        Assert(!fixture.Surface.IsBlocked, "Context expiry should release stale blocker.");
        Assert(fixture.Surface.ResumeCount == 0, "Destroyed controller must not be resumed.");
        Assert(fixture.Surface.BeginCount == 1, "Context expiry must not retry.");

        fixture.Files.MarkUsable("late-context.png");
        attempt.Complete(Success("late-context.png"));
        WaitUntil(
            () => fixture.Files.Deleted.Contains("late-context.png"),
            "Context-expired late artifact should be deleted."
        );
    }

    private static void Summary_replacement_discards_stale_continuation()
    {
        var fixture = ReadyFixture();
        var attempt = fixture.Surface.LastAttempt!;
        fixture.Surface.IsSummaryCurrent = false;
        fixture.Core.OnFrame(fixture.Context);

        Assert(attempt.CancelCount == 1, "Summary replacement should cancel capture.");
        Assert(fixture.Surface.ResumeCount == 0, "A replacement summary must not be advanced.");
        Assert(!fixture.Surface.IsBlocked, "Summary replacement should release the blocker.");
    }

    private static void Controller_replacement_discards_stale_continuation()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Core.OnFrame(fixture.Context);
        fixture.Core.RequestContinue(fixture.Screen, fixture.Context);

        fixture.Surface.ActiveScreen = new FakeScreen
        {
            Id = 8,
            Readiness = Readiness(EndOfRunCaptureReadinessState.Ready),
        };
        fixture.Core.OnFrame(fixture.Context);

        Assert(fixture.Surface.BeginCount == 0, "A replacement controller must cancel capture.");
        Assert(fixture.Surface.ResumeCount == 0, "A replacement controller must not be advanced.");
        Assert(!fixture.Surface.IsBlocked, "Controller replacement must release the blocker.");
    }

    private static void Stale_click_before_frame_is_consumed_once()
    {
        var replacementBeforeFirstClick = new Fixture();
        replacementBeforeFirstClick.Core.OnEndOfRunInitializing();
        replacementBeforeFirstClick.Core.ObserveRevealStarted(replacementBeforeFirstClick.Screen);
        replacementBeforeFirstClick.Screen.Readiness = Readiness(
            EndOfRunCaptureReadinessState.Ready
        );
        replacementBeforeFirstClick.Core.OnFrame(replacementBeforeFirstClick.Context);
        var newController = new FakeScreen
        {
            Id = 8,
            Readiness = Readiness(EndOfRunCaptureReadinessState.Ready),
        };
        replacementBeforeFirstClick.Surface.ActiveScreen = newController;
        Assert(
            replacementBeforeFirstClick.Core.RequestContinue(
                newController,
                replacementBeforeFirstClick.Context
            ),
            "A replacement controller must not advance on the first pre-frame stale click."
        );
        Assert(
            !replacementBeforeFirstClick.Core.RequestContinue(
                newController,
                replacementBeforeFirstClick.Context
            ),
            "A later click may fail open after replacement cleanup."
        );

        var summaryReplacement = ReadyFixture();
        summaryReplacement.Surface.IsSummaryCurrent = false;
        Assert(
            summaryReplacement.Core.RequestContinue(
                summaryReplacement.Screen,
                summaryReplacement.Context
            ),
            "The click that first discovers a replacement summary must be consumed."
        );
        Assert(
            !summaryReplacement.Core.RequestContinue(
                summaryReplacement.Screen,
                summaryReplacement.Context
            ),
            "After stale cleanup, a later manual click should fail open."
        );

        foreach (var clickReplacement in new[] { false, true })
        {
            var controllerReplacement = ReadyFixture();
            var replacement = new FakeScreen
            {
                Id = 8,
                Readiness = Readiness(EndOfRunCaptureReadinessState.Ready),
            };
            controllerReplacement.Surface.ActiveScreen = replacement;
            var clickedScreen = clickReplacement ? replacement : controllerReplacement.Screen;

            Assert(
                controllerReplacement.Core.RequestContinue(
                    clickedScreen,
                    controllerReplacement.Context
                ),
                "A pre-frame click must not advance either the old or replacement controller."
            );
            Assert(
                !controllerReplacement.Core.RequestContinue(
                    clickedScreen,
                    controllerReplacement.Context
                ),
                "A later manual click should fail open after controller replacement cleanup."
            );
            Assert(
                controllerReplacement.Surface.ResumeCount == 0,
                "Stale click cleanup must not auto-resume either controller."
            );
        }
    }

    private static void Competing_active_controller_rejects_native_continuation()
    {
        var beforeClick = new Fixture();
        beforeClick.Core.OnEndOfRunInitializing();
        beforeClick.Core.ObserveRevealStarted(beforeClick.Screen);
        beforeClick.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        beforeClick.Core.OnFrame(beforeClick.Context);
        beforeClick.Surface.HasCompetingActiveController = true;
        Assert(
            beforeClick.Core.RequestContinue(beforeClick.Screen, beforeClick.Context),
            "An ambiguous ready Continue must be consumed instead of advancing the old controller."
        );
        Assert(beforeClick.Surface.BeginCount == 0, "An ambiguous controller must not capture.");
        Assert(beforeClick.Surface.ResumeCount == 0, "An ambiguous controller must not advance.");
        Assert(
            beforeClick.Core.RequestContinue(beforeClick.Screen, beforeClick.Context),
            "Repeated clicks must remain consumed while controller identity is ambiguous."
        );
        beforeClick.Surface.HasCompetingActiveController = false;
        Assert(
            !beforeClick.Core.RequestContinue(beforeClick.Screen, beforeClick.Context),
            "Manual native retry should fail open after controller ambiguity clears."
        );

        var fixture = ReadyFixture();
        fixture.Files.MarkUsable("competing-controller.png");
        fixture.Surface.LastAttempt!.Complete(Success("competing-controller.png"));
        fixture.Core.OnFrame(fixture.Context);
        fixture.Surface.HasCompetingActiveController = true;
        fixture.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());
        fixture.Core.OnFrame(fixture.Context);

        Assert(
            fixture.Surface.ResumeCount == 0,
            "A still-active old controller must not advance after replacement."
        );
        Assert(!fixture.Surface.IsBlocked, "Rejected stale continuation must release the blocker.");
    }

    private static void Ambiguous_controller_context_loss_is_terminal()
    {
        foreach (var makeUnavailable in new[] { false, true })
        {
            var fixture = new Fixture();
            fixture.Core.OnEndOfRunInitializing();
            fixture.Core.ObserveRevealStarted(fixture.Screen);
            fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
            fixture.Core.OnFrame(fixture.Context);
            fixture.Surface.HasCompetingActiveController = true;
            Assert(
                fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
                "Controller ambiguity should consume the first Continue request."
            );

            if (makeUnavailable)
                fixture.Surface.IsAvailable = false;
            fixture.Core.OnFrame(
                makeUnavailable
                    ? fixture.Context
                    : new EndOfRunCaptureContext(false, "run-7", "Vanessa")
            );
            fixture.Surface.IsAvailable = true;

            Assert(
                !fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
                "Disabled context or an unavailable surface must terminally discard ambiguity state."
            );
            Assert(fixture.Surface.BeginCount == 0, "Context loss must not start capture.");
            Assert(
                fixture.Surface.ResumeCount == 0,
                "Context loss must not resume stale Continue."
            );
        }
    }

    private static void Continue_target_probe_failure_allows_manual_retry()
    {
        foreach (var throwOnAmbiguityProbe in new[] { false, true })
        {
            var fixture = new Fixture();
            fixture.Core.OnEndOfRunInitializing();
            fixture.Core.ObserveRevealStarted(fixture.Screen);
            fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
            fixture.Core.OnFrame(fixture.Context);
            fixture.Surface.IsContinueTargetAvailable = false;
            fixture.Surface.ThrowOnAmbiguityProbe = throwOnAmbiguityProbe;

            Assert(
                fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
                "An unsafe target-probe failure should consume only the detecting click."
            );
            Assert(
                !fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
                "Target-probe failure must terminally fail open for a manual retry."
            );
            Assert(fixture.Surface.BeginCount == 0, "A missing Continue target must not capture.");
            Assert(
                fixture.Surface.ResumeCount == 0,
                "A missing Continue target must not auto-resume."
            );
        }

        var screenIdFailure = new Fixture();
        screenIdFailure.Core.OnEndOfRunInitializing();
        screenIdFailure.Core.ObserveRevealStarted(screenIdFailure.Screen);
        screenIdFailure.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        screenIdFailure.Core.OnFrame(screenIdFailure.Context);
        screenIdFailure.Surface.HasCompetingActiveController = true;
        screenIdFailure.Surface.GetScreenIdCallsBeforeThrow = 2;
        Assert(
            screenIdFailure.Core.RequestContinue(screenIdFailure.Screen, screenIdFailure.Context),
            "A competing-target screen-id failure should consume the detecting click."
        );
        Assert(
            !screenIdFailure.Core.RequestContinue(screenIdFailure.Screen, screenIdFailure.Context),
            "A screen-id failure must terminally fail open instead of leaving ambiguity state."
        );

        foreach (var throwOnLaterProbe in new[] { false, true })
        {
            var fixture = new Fixture();
            fixture.Core.OnEndOfRunInitializing();
            fixture.Core.ObserveRevealStarted(fixture.Screen);
            fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
            fixture.Core.OnFrame(fixture.Context);
            fixture.Surface.HasCompetingActiveController = true;
            Assert(
                fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
                "A competing controller should establish persistent ambiguity."
            );

            fixture.Surface.HasCompetingActiveController = false;
            fixture.Surface.IsAmbiguityProbeAvailable = throwOnLaterProbe;
            fixture.Surface.ThrowOnAmbiguityProbe = throwOnLaterProbe;
            Assert(
                fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
                "A later ambiguity-probe failure should consume its detecting click."
            );
            Assert(
                !fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
                "A later ambiguity-probe failure must terminally allow manual retry."
            );
        }

        var replacement = new Fixture();
        replacement.Core.OnEndOfRunInitializing();
        replacement.Core.ObserveRevealStarted(replacement.Screen);
        replacement.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        replacement.Core.OnFrame(replacement.Context);
        replacement.Surface.HasCompetingActiveController = true;
        replacement.Core.RequestContinue(replacement.Screen, replacement.Context);
        var newController = new FakeScreen
        {
            Id = 8,
            Readiness = Readiness(EndOfRunCaptureReadinessState.Ready),
        };
        replacement.Surface.ActiveScreen = newController;
        Assert(
            replacement.Core.RequestContinue(newController, replacement.Context),
            "A new controller click must be consumed while old ambiguity state is discarded."
        );
        Assert(
            !replacement.Core.RequestContinue(newController, replacement.Context),
            "A later click may fail open after ambiguous old-session cleanup."
        );
    }

    private static void Reinitialization_discards_deferred_continuation()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Core.OnFrame(fixture.Context);
        fixture.Core.RequestContinue(fixture.Screen, fixture.Context);

        fixture.Core.OnEndOfRunInitializing();

        Assert(fixture.Surface.BeginCount == 0, "Reinitialization must discard deferred capture.");
        Assert(fixture.Surface.ResumeCount == 0, "Reinitialization must discard continuation.");
        Assert(!fixture.Surface.IsBlocked, "Reinitialization must release the blocker.");
        Assert(
            !fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "The stale workflow must remain fail open after reinitialization."
        );
    }

    private static void End_of_run_exit_discards_stale_continuation()
    {
        var fixture = ReadyFixture();
        fixture.Files.MarkUsable("exit.png");
        fixture.Surface.LastAttempt!.Complete(Success("exit.png"));
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Persistence.CallCount == 1, "The fixture should be persisting metadata.");

        fixture.Surface.ActiveScreen = null;
        fixture.Core.OnFrame(fixture.Context);

        Assert(fixture.Surface.ResumeCount == 0, "EndOfRun exit must discard stale continuation.");
        Assert(!fixture.Surface.IsBlocked, "EndOfRun exit must release the blocker.");
        Assert(
            !fixture.Files.Deleted.Contains("exit.png"),
            "A verified artifact must survive EndOfRun exit during metadata persistence."
        );

        fixture.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());
        fixture.Core.OnFrame(fixture.Context);
        Assert(
            fixture.Surface.ResumeCount == 0,
            "Late metadata must not resume after EndOfRun exit."
        );
    }

    private static void Reset_cancels_the_old_generation_and_allows_a_new_run()
    {
        var fixture = ReadyFixture();
        var oldAttempt = fixture.Surface.LastAttempt!;
        fixture.Core.OnRunStarted();
        Assert(oldAttempt.CancelCount == 1, "Run reset should cancel the old generation.");
        Assert(!fixture.Surface.IsBlocked, "Run reset should release Continue.");
        Assert(fixture.Surface.ResumeCount == 0, "Run reset must discard stale continuation.");

        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Core.OnFrame(fixture.Context);
        fixture.Core.RequestContinue(fixture.Screen, fixture.Context);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 2, "New run should get a fresh capture generation.");
    }

    private static void Native_continue_no_op_is_diagnostic_and_fail_open()
    {
        using var log = new TestLogCapture();
        var fixture = ReadyFixture();
        fixture.Surface.ResumeOutcome = new EndOfRunContinueResumeOutcome(
            EndOfRunContinueResumeStatus.NativeNoOp
        );
        fixture.Files.MarkUsable("native-no-op.png");
        fixture.Surface.LastAttempt!.Complete(Success("native-no-op.png"));
        fixture.Core.OnFrame(fixture.Context);
        fixture.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());
        fixture.Core.OnFrame(fixture.Context);

        Assert(fixture.Surface.ResumeCount == 1, "Native Continue should be invoked at most once.");
        Assert(!fixture.Surface.IsBlocked, "A native no-op must leave manual Continue available.");
        Assert(
            !fixture.Core.RequestContinue(fixture.Screen, fixture.Context),
            "Terminal workflow must fail open for a manual retry."
        );
        Assert(
            log.Events.Count(entry =>
                entry.Contains(
                    "event=screenshots.capture.continue_resume_failed",
                    StringComparison.Ordinal
                )
            ) == 1,
            "A native no-op should emit one diagnostic event."
        );
    }

    private static void Disabled_and_non_summary_continue_are_fail_open()
    {
        var disabled = new Fixture();
        disabled.Core.OnEndOfRunInitializing();
        Assert(
            !disabled.Core.RequestContinue(
                disabled.Screen,
                new EndOfRunCaptureContext(false, "run-7", "Vanessa")
            ),
            "Disabled screenshots must preserve native Continue."
        );
        Assert(disabled.Surface.BeginCount == 0, "Disabled screenshots must not capture.");

        var nonSummary = new Fixture();
        nonSummary.Core.OnEndOfRunInitializing();
        nonSummary.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.NotSummary);
        Assert(
            !nonSummary.Core.RequestContinue(nonSummary.Screen, nonSummary.Context),
            "Non-summary pages must preserve native Continue."
        );
        Assert(nonSummary.Surface.BeginCount == 0, "Non-summary pages must not capture.");

        var disabledAfterReady = new Fixture();
        disabledAfterReady.Core.OnEndOfRunInitializing();
        disabledAfterReady.Core.ObserveRevealStarted(disabledAfterReady.Screen);
        disabledAfterReady.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        disabledAfterReady.Core.OnFrame(disabledAfterReady.Context);
        Assert(
            !disabledAfterReady.Core.RequestContinue(
                disabledAfterReady.Screen,
                new EndOfRunCaptureContext(false, "run-7", "Vanessa")
            ),
            "Disabling screenshots after readiness must fail open on the current click."
        );

        var nonSummaryAfterReady = new Fixture();
        nonSummaryAfterReady.Core.OnEndOfRunInitializing();
        nonSummaryAfterReady.Core.ObserveRevealStarted(nonSummaryAfterReady.Screen);
        nonSummaryAfterReady.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        nonSummaryAfterReady.Core.OnFrame(nonSummaryAfterReady.Context);
        nonSummaryAfterReady.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.NotSummary);
        Assert(
            !nonSummaryAfterReady.Core.RequestContinue(
                nonSummaryAfterReady.Screen,
                nonSummaryAfterReady.Context
            ),
            "A page that became non-summary after readiness must fail open on the current click."
        );
    }

    private static void Terminal_outcome_is_emitted_once()
    {
        using var log = new TestLogCapture();
        var fixture = ReadyFixture();
        fixture.Surface.LastAttempt!.Complete(
            EndOfRunCaptureAttemptOutcome.Failed(
                ScreenshotCaptureReasonCode.CaptureTaskFaulted,
                new InvalidOperationException("first")
            )
        );
        fixture.Core.OnFrame(fixture.Context);
        fixture.Clock.Advance(1f);
        fixture.Core.OnFrame(fixture.Context);
        fixture.Surface.LastAttempt!.Complete(
            EndOfRunCaptureAttemptOutcome.Failed(ScreenshotCaptureReasonCode.CaptureReturnedNull)
        );
        fixture.Core.OnFrame(fixture.Context);
        fixture.Core.OnFrame(fixture.Context);

        var terminals = log
            .Events.Where(entry =>
                entry.Contains("event=screenshots.capture.failed", StringComparison.Ordinal)
            )
            .ToArray();
        Assert(terminals.Length == 1, "One run must emit exactly one terminal outcome.");
        Assert(
            terminals[0].Contains("attempt_count=2", StringComparison.Ordinal),
            "Terminal outcome should report the bounded attempt count."
        );
        Assert(fixture.Surface.ResumeCount == 1, "Terminal failure should resume exactly once.");
    }

    private static Fixture ReadyFixture()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Core.OnFrame(fixture.Context);
        fixture.Core.RequestContinue(fixture.Screen, fixture.Context);
        fixture.Core.OnFrame(fixture.Context);
        return fixture;
    }

    private static EndOfRunCaptureReadinessOutcome Readiness(EndOfRunCaptureReadinessState state) =>
        new(state, null, null);

    private static EndOfRunCaptureAttemptOutcome Success(string path) =>
        EndOfRunCaptureAttemptOutcome.Succeeded(
            new ScreenshotCaptureResult { ScreenshotId = "shot", FilePath = path }
        );

    private static void WaitUntil(Func<bool> predicate, string message)
    {
        if (SpinWait.SpinUntil(predicate, TimeSpan.FromSeconds(2)))
            return;
        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        internal Fixture()
        {
            Surface.ActiveScreen = Screen;
            Core = new EndOfRunCaptureWorkflowCore<FakeScreen>(
                Persistence,
                Clock,
                Files,
                EndOfRunCapturePolicy.Default
            );
            Core.AttachSurface(Surface);
        }

        internal FakeClock Clock { get; } = new();
        internal FakeFileSystem Files { get; } = new();
        internal FakePersistence Persistence { get; } = new();
        internal FakeSurface Surface { get; } = new();
        internal FakeScreen Screen { get; } = new() { Id = 7 };
        internal EndOfRunCaptureWorkflowCore<FakeScreen> Core { get; }
        internal EndOfRunCaptureContext Context { get; } = new(true, "run-7", "Vanessa");
    }

    private sealed class FakeScreen
    {
        internal int Id { get; init; }
        internal bool IsActive { get; set; } = true;
        internal EndOfRunCaptureReadinessOutcome Readiness { get; set; } =
            EndOfRunCaptureWorkflowBehaviorTests.Readiness(
                EndOfRunCaptureReadinessState.RevealNotStarted
            );
    }

    private sealed class FakeSurface : IEndOfRunCaptureSurface<FakeScreen>
    {
        internal FakeScreen? ActiveScreen { get; set; }
        internal int BeginCount { get; private set; }
        internal bool IsBlocked { get; private set; }
        internal ManualAttempt? LastAttempt { get; private set; }
        internal Exception? ThrowOnNextBegin { get; set; }
        internal bool NextAttemptHasStarted { get; set; } = true;
        internal int UnblockFailuresRemaining { get; set; }
        internal int UnblockCount { get; private set; }
        internal int ResumeCount { get; private set; }
        internal bool IsSummaryCurrent { get; set; } = true;
        internal bool HasCompetingActiveController { get; set; }
        internal bool IsContinueTargetAvailable { get; set; } = true;
        internal bool IsAmbiguityProbeAvailable { get; set; } = true;
        internal bool ThrowOnAmbiguityProbe { get; set; }
        internal int GetScreenIdCallsBeforeThrow { get; set; } = -1;
        internal EndOfRunContinueResumeOutcome ResumeOutcome { get; set; } =
            new(EndOfRunContinueResumeStatus.Advanced);

        public bool IsAvailable { get; set; } = true;

        public FakeScreen? FindActiveScreen() => ActiveScreen;

        public bool IsScreenActive(FakeScreen screen) => screen.IsActive;

        public int GetScreenId(FakeScreen screen)
        {
            if (GetScreenIdCallsBeforeThrow == 0)
                throw new InvalidOperationException("screen id failed");
            if (GetScreenIdCallsBeforeThrow > 0)
                GetScreenIdCallsBeforeThrow--;
            return screen.Id;
        }

        public EndOfRunCaptureReadinessOutcome GetReadiness(
            FakeScreen screen,
            bool hasRevealStarted
        ) => screen.Readiness;

        public bool TryGetContinueTarget(FakeScreen screen, out EndOfRunContinueTarget target)
        {
            target = new EndOfRunContinueTarget(screen.Id, screen.Id * 10);
            return IsContinueTargetAvailable && IsSummaryCurrent && !HasCompetingActiveController;
        }

        public bool TryGetContinueTargetAmbiguity(FakeScreen screen, out bool ambiguous)
        {
            if (ThrowOnAmbiguityProbe)
                throw new InvalidOperationException("ambiguity probe failed");
            ambiguous = HasCompetingActiveController;
            return IsAmbiguityProbeAvailable;
        }

        public bool IsContinueTargetCurrent(FakeScreen screen, EndOfRunContinueTarget target) =>
            IsSummaryCurrent
            && ReferenceEquals(ActiveScreen, screen)
            && screen.IsActive
            && target.ScreenId == screen.Id
            && target.SummaryId == screen.Id * 10;

        public IEndOfRunCaptureAttempt BeginCapture(
            FakeScreen screen,
            ScreenshotCaptureRequest request
        )
        {
            BeginCount++;
            if (ThrowOnNextBegin != null)
            {
                var failure = ThrowOnNextBegin;
                ThrowOnNextBegin = null;
                throw failure;
            }

            return LastAttempt = new ManualAttempt { HasCaptureStarted = NextAttemptHasStarted };
        }

        public EndOfRunContinueResumeOutcome ResumeContinue(
            FakeScreen screen,
            EndOfRunContinueTarget target
        )
        {
            if (HasCompetingActiveController || !IsContinueTargetCurrent(screen, target))
                return new EndOfRunContinueResumeOutcome(EndOfRunContinueResumeStatus.ContextStale);
            ResumeCount++;
            return ResumeOutcome;
        }

        public void SetContinueBlocked(FakeScreen? screen, bool blocked)
        {
            if (!blocked)
            {
                UnblockCount++;
                if (UnblockFailuresRemaining > 0)
                {
                    UnblockFailuresRemaining--;
                    throw new InvalidOperationException("transient unblock failure");
                }
            }
            IsBlocked = blocked;
        }
    }

    private sealed class ManualAttempt : IEndOfRunCaptureAttempt
    {
        private readonly TaskCompletionSource<EndOfRunCaptureAttemptOutcome> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task<EndOfRunCaptureAttemptOutcome> Completion => _completion.Task;
        public bool HasCaptureStarted { get; set; } = true;
        internal int CancelCount { get; private set; }
        internal int RestoreCount { get; private set; }
        internal Exception? ThrowOnRestore { get; set; }

        public void Cancel() => CancelCount++;

        public void RestoreUi()
        {
            RestoreCount++;
            if (ThrowOnRestore != null)
                throw ThrowOnRestore;
        }

        internal void Complete(EndOfRunCaptureAttemptOutcome outcome) =>
            _completion.TrySetResult(outcome);
    }

    private sealed class FakePersistence : IEndOfRunArtifactPersistence
    {
        private readonly Queue<
            TaskCompletionSource<ScreenshotMetadataPersistenceOutcome>
        > _pending = new();

        internal int CallCount { get; private set; }

        public Task<ScreenshotMetadataPersistenceOutcome> PersistAsync(
            ScreenshotCaptureResult capture,
            bool isPrimary
        )
        {
            CallCount++;
            var completion = new TaskCompletionSource<ScreenshotMetadataPersistenceOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _pending.Enqueue(completion);
            return completion.Task;
        }

        internal void CompleteNext(ScreenshotMetadataPersistenceOutcome outcome) =>
            _pending.Dequeue().TrySetResult(outcome);
    }

    private sealed class FakeClock : IEndOfRunCaptureClock
    {
        public float UnscaledSeconds { get; private set; }
        public float RealtimeSeconds { get; private set; }
        public long Milliseconds => (long)Math.Round(RealtimeSeconds * 1000d);

        internal void Advance(float seconds)
        {
            UnscaledSeconds += seconds;
            RealtimeSeconds += seconds;
        }
    }

    private sealed class FakeFileSystem : IEndOfRunCaptureFileSystem
    {
        private readonly HashSet<string> _usable = new(StringComparer.Ordinal);
        internal HashSet<string> Deleted { get; } = new(StringComparer.Ordinal);

        public bool IsUsablePng(string? filePath) => filePath != null && _usable.Contains(filePath);

        public void DeleteIfExists(string? filePath)
        {
            if (filePath != null)
                Deleted.Add(filePath);
        }

        internal void MarkUsable(string path) => _usable.Add(path);
    }

    private sealed class TestLogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("EndOfRunCaptureWorkflow.Tests");
        private readonly List<string> _events = new();

        internal TestLogCapture()
        {
            _source.LogEvent += OnLogEvent;
            BppLog.Install(_source);
        }

        internal IReadOnlyList<string> Events => _events;

        public void Dispose()
        {
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args) =>
            _events.Add(args.Data?.ToString() ?? string.Empty);
    }
}
