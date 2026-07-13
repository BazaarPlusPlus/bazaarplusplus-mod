#nullable enable
using System.IO;
using System.Reflection;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Storage.RunScreenshot;

var assembly = Assembly.Load("BazaarPlusPlus");
var gateType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunScreenshotGate",
    throwOnError: true
)!;
var gate =
    Activator.CreateInstance(gateType)
    ?? throw new InvalidOperationException("Failed to construct EndOfRunScreenshotGate.");
var summaryRevealDetectorType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunSummaryRevealDetector",
    throwOnError: true
)!;
var summaryRevealStateType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunSummaryRevealState",
    throwOnError: true
)!;
var captureReadinessDetectorType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunCaptureReadinessDetector",
    throwOnError: true
)!;
var captureReadinessStateType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunCaptureReadinessState",
    throwOnError: true
)!;

ScreenshotCaptureOperationContractTests.Run(assembly);

var readyCard = new TheBazaar.UI.EndOfRun.FakeItemController(true);
var readyController = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
    transitionCount: 0,
    new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(readyCard)
);
var readyState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    readyController,
    hasSummaryRevealStarted: false
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        readyState,
        isCaptureEnabled: false,
        nowSeconds: 0f,
        fallbackTimeoutSeconds: 20f
    ),
    "Disabled end-of-run screenshots should not start an automatic capture."
);
Assert(
    !InvokeIsAttemptInFlight(gateType, gate),
    "Disabled end-of-run screenshots should not leave a capture attempt in flight."
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        readyState,
        isCaptureEnabled: true,
        nowSeconds: 0f,
        fallbackTimeoutSeconds: 20f
    ),
    "A ready summary should not capture before the end-of-run event arms the gate."
);

InvokeArmForEndOfRun(gateType, gate);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        readyState,
        isCaptureEnabled: true,
        nowSeconds: 0f,
        fallbackTimeoutSeconds: 20f
    ),
    "Even an all-face-up board must not capture before native summary display starts."
);
readyController.SetTransitionCount(1);
Assert(
    GetEnumName(
        captureReadinessStateType,
        InvokeGetCaptureReadiness(
            captureReadinessDetectorType,
            readyController,
            hasSummaryRevealStarted: true
        )
    ) == "TransitionInProgress",
    "A reveal-start latch must not bypass an active native transition."
);
readyController.SetTransitionCount(0);
var timelineSummary = new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(null, null);
var timelineController = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
    transitionCount: 1,
    timelineSummary
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        InvokeGetCaptureReadiness(
            captureReadinessDetectorType,
            timelineController,
            hasSummaryRevealStarted: false
        ),
        isCaptureEnabled: true,
        nowSeconds: 0f,
        fallbackTimeoutSeconds: 20f
    ),
    "An all-null preloaded card array must not capture while the native transition is active."
);

timelineController.SetTransitionCount(0);
var revealNotStartedState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    timelineController,
    hasSummaryRevealStarted: false
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        revealNotStartedState,
        isCaptureEnabled: true,
        nowSeconds: 1f,
        fallbackTimeoutSeconds: 20f
    ),
    "Transition completion alone must not capture an empty board before native display starts."
);
Assert(
    InvokeShouldBlockContinue(
        gateType,
        gate,
        revealNotStartedState,
        isCaptureEnabled: true,
        nowSeconds: 1f,
        fallbackTimeoutSeconds: 20f
    ),
    "Continue should remain blocked between transition completion and native display start."
);
var timelineCard = new TheBazaar.UI.EndOfRun.FakeItemController(false);
timelineSummary.SetLoadedCards(timelineCard);
InvokeMarkSummaryRevealStarted(gateType, gate);
var timelineRevealState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    timelineController,
    hasSummaryRevealStarted: InvokeHasSummaryRevealStarted(gateType, gate)
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        timelineRevealState,
        isCaptureEnabled: true,
        nowSeconds: 1.1f,
        fallbackTimeoutSeconds: 20f
    ),
    "The same summary instance should remain blocked after its loaded-card array is populated."
);
timelineCard.Animator.SetFaceUp(true);
var timelineReadyState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    timelineController,
    hasSummaryRevealStarted: true
);
Assert(
    InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        timelineReadyState,
        isCaptureEnabled: true,
        nowSeconds: 1.2f,
        fallbackTimeoutSeconds: 20f
    ),
    "The same summary instance should capture after its loaded card becomes face-up."
);
Assert(
    InvokeIsAttemptInFlight(gateType, gate),
    "A started automatic capture should be marked in flight."
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        timelineReadyState,
        isCaptureEnabled: true,
        nowSeconds: 1.3f,
        fallbackTimeoutSeconds: 20f
    ),
    "Only one automatic capture attempt should be in flight at a time."
);

InvokeCompleteCaptureAttempt(gateType, gate);
Assert(
    !InvokeIsAttemptInFlight(gateType, gate),
    "Completing an automatic capture should immediately clear in-flight suppression."
);
Assert(
    !InvokeShouldBlockContinue(
        gateType,
        gate,
        timelineReadyState,
        isCaptureEnabled: true,
        nowSeconds: 1.3f,
        fallbackTimeoutSeconds: 20f
    ),
    "A completed automatic capture should release Continue without a passthrough click."
);
InvokeArmForEndOfRun(gateType, gate);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        timelineReadyState,
        isCaptureEnabled: true,
        nowSeconds: 2f,
        fallbackTimeoutSeconds: 20f
    ),
    "A completed run must not capture again after a duplicate arm event."
);

InvokeResetForNewRun(gateType, gate);
InvokeArmForEndOfRun(gateType, gate);
var emptyBoardSummary = new TheBazaar.UI.EndOfRun.EndOfRunSummaryController();
var skillSequence = new TheBazaar.UI.EndOfRun.FakeSequence(duration: 1f, isComplete: false);
emptyBoardSummary.SetSkillSequence(skillSequence);
var emptyBoardController = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
    transitionCount: 0,
    emptyBoardSummary
);
var emptyBoardNotStartedState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    emptyBoardController,
    hasSummaryRevealStarted: false
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        emptyBoardNotStartedState,
        isCaptureEnabled: true,
        nowSeconds: 3f,
        fallbackTimeoutSeconds: 20f
    ),
    "A genuinely empty board must wait for native display start."
);
InvokeMarkSummaryRevealStarted(gateType, gate);
var emptyBoardAnimatingState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    emptyBoardController,
    hasSummaryRevealStarted: true
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        emptyBoardAnimatingState,
        isCaptureEnabled: true,
        nowSeconds: 3.1f,
        fallbackTimeoutSeconds: 20f
    ),
    "An empty item board must still wait for the native skill scale animation."
);
skillSequence.SetComplete(true);
var emptyBoardReadyState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    emptyBoardController,
    hasSummaryRevealStarted: true
);
Assert(
    InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        emptyBoardReadyState,
        isCaptureEnabled: true,
        nowSeconds: 3.2f,
        fallbackTimeoutSeconds: 20f
    ),
    "An empty board should capture after native display starts and its skill sequence settles."
);
InvokeCompleteCaptureAttempt(gateType, gate);

var faceUpSkillSummary = new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
    new TheBazaar.UI.EndOfRun.FakeItemController(true)
);
var faceUpSkillSequence = new TheBazaar.UI.EndOfRun.FakeSequence(duration: 1f, isComplete: false);
faceUpSkillSummary.SetSkillSequence(faceUpSkillSequence);
var faceUpSkillController = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
    transitionCount: 0,
    faceUpSkillSummary
);
Assert(
    GetEnumName(
        captureReadinessStateType,
        InvokeGetCaptureReadiness(
            captureReadinessDetectorType,
            faceUpSkillController,
            hasSummaryRevealStarted: true
        )
    ) == "RevealInProgress",
    "Face-up items must not bypass an in-progress skill reveal sequence."
);
faceUpSkillSequence.SetComplete(true);
Assert(
    GetEnumName(
        captureReadinessStateType,
        InvokeGetCaptureReadiness(
            captureReadinessDetectorType,
            faceUpSkillController,
            hasSummaryRevealStarted: true
        )
    ) == "Ready",
    "A face-up item board should become ready after its skill reveal also completes."
);

InvokeResetForNewRun(gateType, gate);
InvokeArmForEndOfRun(gateType, gate);
InvokeMarkSummaryRevealStarted(gateType, gate);
timelineCard.Animator.SetFaceUp(false);
var revealingState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    timelineController,
    hasSummaryRevealStarted: true
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        revealingState,
        isCaptureEnabled: true,
        nowSeconds: 9.1f,
        fallbackTimeoutSeconds: 20f
    ),
    "A fixed eight-second window must not capture while a summary card is still face-down."
);
Assert(
    InvokeShouldBlockContinue(
        gateType,
        gate,
        revealingState,
        isCaptureEnabled: true,
        nowSeconds: 9.1f,
        fallbackTimeoutSeconds: 20f
    ),
    "Continue should remain blocked while the summary reveal is in progress."
);

InvokeResetForNewRun(gateType, gate);
InvokeArmForEndOfRun(gateType, gate);
InvokeMarkSummaryRevealStarted(gateType, gate);
revealingState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    timelineController,
    hasSummaryRevealStarted: true
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        revealingState,
        isCaptureEnabled: true,
        nowSeconds: 30f,
        fallbackTimeoutSeconds: 20f
    ),
    "A stuck reveal should begin a bounded wait without capturing."
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        revealingState,
        isCaptureEnabled: true,
        nowSeconds: 50f,
        fallbackTimeoutSeconds: 20f
    ),
    "A known face-down card must never become a timeout screenshot target."
);
Assert(
    InvokeHasFinishedForCurrentRun(gateType, gate)
        && !InvokeShouldBlockContinue(
            gateType,
            gate,
            revealingState,
            isCaptureEnabled: true,
            nowSeconds: 50f,
            fallbackTimeoutSeconds: 20f
        ),
    "A stuck known reveal should fail open at the deadline instead of trapping Continue."
);

InvokeResetForNewRun(gateType, gate);
InvokeArmForEndOfRun(gateType, gate);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        emptyBoardNotStartedState,
        isCaptureEnabled: true,
        nowSeconds: 60f,
        fallbackTimeoutSeconds: 20f
    ),
    "A missing native display-start signal should begin a bounded wait."
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        emptyBoardNotStartedState,
        isCaptureEnabled: true,
        nowSeconds: 80f,
        fallbackTimeoutSeconds: 20f
    )
        && InvokeHasFinishedForCurrentRun(gateType, gate)
        && !InvokeShouldBlockContinue(
            gateType,
            gate,
            emptyBoardNotStartedState,
            isCaptureEnabled: true,
            nowSeconds: 80f,
            fallbackTimeoutSeconds: 20f
        ),
    "A missing reveal-start patch must fail open without capturing or trapping Continue."
);

InvokeResetForNewRun(gateType, gate);
InvokeArmForEndOfRun(gateType, gate);
var notSummaryState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
        transitionCount: 0,
        new TheBazaar.UI.EndOfRun.OtherEndOfRunController()
    ),
    hasSummaryRevealStarted: false
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        notSummaryState,
        isCaptureEnabled: true,
        nowSeconds: 999f,
        fallbackTimeoutSeconds: 20f
    ),
    "A non-summary screen must never become a timeout screenshot target."
);
Assert(
    !InvokeShouldBlockContinue(
        gateType,
        gate,
        notSummaryState,
        isCaptureEnabled: true,
        nowSeconds: 999f,
        fallbackTimeoutSeconds: 20f
    ),
    "A known non-summary page should never be blocked by the screenshot gate."
);

InvokeResetForNewRun(gateType, gate);
InvokeArmForEndOfRun(gateType, gate);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        revealNotStartedState,
        isCaptureEnabled: true,
        nowSeconds: 1000f,
        fallbackTimeoutSeconds: 20f
    ),
    "Observing a summary should not capture before its display starts."
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        notSummaryState,
        isCaptureEnabled: true,
        nowSeconds: 1001f,
        fallbackTimeoutSeconds: 20f
    )
        && InvokeHasFinishedForCurrentRun(gateType, gate)
        && !InvokeShouldBlockContinue(
            gateType,
            gate,
            notSummaryState,
            isCaptureEnabled: true,
            nowSeconds: 1001f,
            fallbackTimeoutSeconds: 20f
        ),
    "Leaving a previously observed summary should fail open without capturing another page."
);

var throwingController = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
    transitionCount: 0,
    new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
        new TheBazaar.UI.EndOfRun.ThrowingItemController()
    )
);
Assert(
    GetEnumName(
        captureReadinessStateType,
        InvokeGetCaptureReadiness(
            captureReadinessDetectorType,
            throwingController,
            hasSummaryRevealStarted: false
        )
    ) == "RevealNotStarted"
        && GetEnumName(
            captureReadinessStateType,
            InvokeGetCaptureReadiness(
                captureReadinessDetectorType,
                throwingController,
                hasSummaryRevealStarted: true
            )
        ) == "DetectionFailed",
    "Reflection exceptions must not bypass the native display-start latch."
);

InvokeResetForNewRun(gateType, gate);
InvokeArmForEndOfRun(gateType, gate);
var unknownTargetState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    new object(),
    hasSummaryRevealStarted: false
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        unknownTargetState,
        isCaptureEnabled: true,
        nowSeconds: 100f,
        fallbackTimeoutSeconds: 20f
    ),
    "An unknown target should fail open instead of blocking or capturing an arbitrary page."
);
Assert(
    InvokeHasFinishedForCurrentRun(gateType, gate)
        && !InvokeShouldBlockContinue(
            gateType,
            gate,
            unknownTargetState,
            isCaptureEnabled: true,
            nowSeconds: 999f,
            fallbackTimeoutSeconds: 20f
        ),
    "Unknown-target fail-open should remain released at any later deadline."
);

InvokeResetForNewRun(gateType, gate);
InvokeArmForEndOfRun(gateType, gate);
InvokeMarkSummaryRevealStarted(gateType, gate);
var detectionFailureController = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
    transitionCount: 0,
    new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
        new TheBazaar.UI.EndOfRun.BadItemController()
    )
);
var detectionFailureState = InvokeGetCaptureReadiness(
    captureReadinessDetectorType,
    detectionFailureController,
    hasSummaryRevealStarted: true
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        detectionFailureState,
        isCaptureEnabled: true,
        nowSeconds: 100f,
        fallbackTimeoutSeconds: 20f
    ),
    "A known-summary detection failure should start a bounded wait, not capture immediately."
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        detectionFailureState,
        isCaptureEnabled: true,
        nowSeconds: 119.99f,
        fallbackTimeoutSeconds: 20f
    ),
    "A known-summary detection failure should remain blocked until its fallback deadline."
);
Assert(
    InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        detectionFailureState,
        isCaptureEnabled: true,
        nowSeconds: 120f,
        fallbackTimeoutSeconds: 20f
    ),
    "A known-summary detection failure should use a bounded capture fallback."
);
Assert(
    InvokeAbortCaptureAttempt(gateType, gate, retryAvailableAtSeconds: 121f),
    "The first capture failure should schedule one bounded retry."
);
Assert(
    !InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        detectionFailureState,
        isCaptureEnabled: true,
        nowSeconds: 120.99f,
        fallbackTimeoutSeconds: 20f
    ),
    "A failed capture should respect its retry cooldown."
);
Assert(
    InvokeTryBeginAutomaticCapture(
        gateType,
        gate,
        detectionFailureState,
        isCaptureEnabled: true,
        nowSeconds: 121f,
        fallbackTimeoutSeconds: 20f
    ),
    "The fallback capture should retry once after the cooldown."
);
Assert(
    !InvokeAbortCaptureAttempt(gateType, gate, retryAvailableAtSeconds: 122f),
    "A second capture failure should fail open instead of retrying forever."
);
Assert(
    !InvokeShouldBlockContinue(
        gateType,
        gate,
        detectionFailureState,
        isCaptureEnabled: true,
        nowSeconds: 122f,
        fallbackTimeoutSeconds: 20f
    ),
    "Fail-open should release Continue after the bounded retry budget is exhausted."
);

var pathBuilderType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.ScreenshotPathBuilder",
    throwOnError: true
)!;
var captureSourceType = Assembly
    .Load("BazaarPlusPlus.Storage")
    .GetType(
        "BazaarPlusPlus.Storage.RunScreenshot.RunScreenshotCaptureSource",
        throwOnError: true
    )!;
Assert(
    Enum.GetNames(captureSourceType) is ["EndOfRunAuto"],
    "Screenshot capture sources should only expose EndOfRunAuto."
);
var captureResultType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.ScreenshotCaptureResult",
    throwOnError: true
)!;
var basicsType = assembly.GetType(
    "BazaarPlusPlus.Core.GameState.RunBasicsSnapshot",
    throwOnError: true
)!;
var recordMapperType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.RunScreenshotRecordMapper",
    throwOnError: true
)!;
var captureResult = Activator.CreateInstance(captureResultType)!;
var capturedAtLocal = new DateTimeOffset(2026, 7, 11, 20, 30, 0, TimeSpan.FromHours(8));
var capturedAtUtc = capturedAtLocal.ToUniversalTime();
SetProperty(captureResultType, captureResult, "ScreenshotId", "shot-42");
SetProperty(captureResultType, captureResult, "RunId", "run-42");
SetProperty(captureResultType, captureResult, "HeroName", null);
SetProperty(captureResultType, captureResult, "BattleId", "battle-42");
SetProperty(
    captureResultType,
    captureResult,
    "CaptureSource",
    Enum.Parse(captureSourceType, "EndOfRunAuto")
);
SetProperty(captureResultType, captureResult, "RelativePath", "2026-07-11/final.png");
SetProperty(captureResultType, captureResult, "CapturedAtLocal", capturedAtLocal);
SetProperty(captureResultType, captureResult, "CapturedAtUtc", capturedAtUtc);
var basics = Activator.CreateInstance(basicsType)!;
SetProperty(basicsType, basics, "Day", 12);
SetProperty(basicsType, basics, "Victories", 10);
SetProperty(basicsType, basics, "Hero", "Vanessa");
var rank = new RankSnapshot { Rank = "Legend", Rating = 2750 };
var createRecord = recordMapperType.GetMethod(
    "CreateRecord",
    BindingFlags.Public | BindingFlags.Static
)!;
var screenshotRecord = (RunScreenshotRecord)
    createRecord.Invoke(null, [captureResult, basics, rank, 37, true, "Online"])!;
Assert(
    screenshotRecord.ScreenshotId == "shot-42"
        && screenshotRecord.RunId == "run-42"
        && screenshotRecord.HeroName == "Vanessa"
        && screenshotRecord.BattleId == "battle-42"
        && screenshotRecord.IsPrimary
        && screenshotRecord.ImageRelativePath == "2026-07-11/final.png"
        && screenshotRecord.CapturedAtLocal == capturedAtLocal
        && screenshotRecord.CapturedAtUtc == capturedAtUtc
        && screenshotRecord.Day == 12
        && screenshotRecord.PlayerRank == "Legend"
        && screenshotRecord.PlayerRating == 2750
        && screenshotRecord.PlayerPosition == 37
        && screenshotRecord.VictoriesAtCapture == 10
        && screenshotRecord.BuildChannel == "Online",
    "The screenshot record mapper should preserve capture data and supplied run snapshots."
);
SetProperty(captureResultType, captureResult, "HeroName", "Buffed Banana");
var namedHeroRecord = (RunScreenshotRecord)
    createRecord.Invoke(null, [captureResult, basics, rank, 37, true, "Online"])!;
Assert(
    namedHeroRecord.HeroName == "Buffed Banana",
    "A non-blank capture hero name should win over the snapshot hero."
);
var nullBasicsRecord = (RunScreenshotRecord)
    createRecord.Invoke(null, [captureResult, null, rank, null, false, "Online"])!;
Assert(
    nullBasicsRecord.Day == null
        && nullBasicsRecord.VictoriesAtCapture == null
        && nullBasicsRecord.HeroName == "Buffed Banana"
        && nullBasicsRecord.ScreenshotId == "shot-42",
    "Missing run basics should null day and victories while capture data survives."
);
var screenshotPath = InvokeBuildRelativePath(
    pathBuilderType,
    runId: "Run-42/Final",
    capturedAtLocal: new DateTimeOffset(2026, 4, 7, 21, 30, 15, TimeSpan.FromHours(8))
);
Assert(
    screenshotPath
        == Path.Combine("2026-04-07", "2026-04-07_21-30-15-000_final_run-run-42final.png"),
    $"Unexpected screenshot path: {screenshotPath}"
);

var fallbackPath = InvokeBuildRelativePath(
    pathBuilderType,
    runId: null,
    capturedAtLocal: new DateTimeOffset(2026, 4, 7, 9, 5, 4, TimeSpan.FromHours(-7))
);
Assert(
    fallbackPath == Path.Combine("2026-04-07", "2026-04-07_09-05-04-000_final_run-anonymous.png"),
    $"Expected anonymous fallback path, got: {fallbackPath}"
);

Assert(
    InvokeGetSummaryRevealState(summaryRevealDetectorType, summaryRevealStateType, new object())
        == "TargetDetectionFailed",
    "A missing active-controller seam should be distinguished from a known non-summary page."
);
Assert(
    InvokeGetSummaryRevealState(
        summaryRevealDetectorType,
        summaryRevealStateType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.OtherEndOfRunController()
        )
    ) == "NotSummary",
    "Non-summary end-of-run screens should not be treated as summary reveal work."
);
Assert(
    InvokeGetSummaryRevealState(
        summaryRevealDetectorType,
        summaryRevealStateType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
                new TheBazaar.UI.EndOfRun.FakeItemController(false)
            )
        )
    ) == "RevealInProgress",
    "Summary capture should stay blocked until every loaded card is face-up."
);
Assert(
    InvokeGetSummaryRevealState(
        summaryRevealDetectorType,
        summaryRevealStateType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
                null,
                new TheBazaar.UI.EndOfRun.FakeItemController(false)
            )
        )
    ) == "RevealInProgress",
    "Any unrevealed summary card should keep continue blocked even when some slots are empty."
);
Assert(
    InvokeGetSummaryRevealState(
        summaryRevealDetectorType,
        summaryRevealStateType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
                new TheBazaar.UI.EndOfRun.FakeItemController(true),
                null,
                new TheBazaar.UI.EndOfRun.FakeItemController(true)
            )
        )
    ) == "RevealComplete",
    "Summary capture should be allowed once all loaded cards are face-up."
);
Assert(
    InvokeGetSummaryRevealState(
        summaryRevealDetectorType,
        summaryRevealStateType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
                new TheBazaar.UI.EndOfRun.FakeFieldItemController(true)
            )
        )
    ) == "RevealComplete",
    "Summary capture should also work when Animator is exposed as a field."
);
Assert(
    InvokeGetSummaryRevealState(
        summaryRevealDetectorType,
        summaryRevealStateType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController()
        )
    ) == "NoLoadedCards",
    "Empty and all-null summary boards should require the native reveal-start latch."
);
Assert(
    InvokeGetSummaryRevealState(
        summaryRevealDetectorType,
        summaryRevealStateType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
                new TheBazaar.UI.EndOfRun.BadItemController()
            )
        )
    ) == "DetectionFailed",
    "Missing summary reflection members should be surfaced as detection failures."
);
Assert(
    Enum.GetNames(summaryRevealStateType)
        is [
            "TargetDetectionFailed",
            "NotSummary",
            "NoLoadedCards",
            "RevealInProgress",
            "RevealComplete",
            "DetectionFailed",
        ],
    "Summary reveal state enum should expose the expected states."
);
Assert(
    Enum.GetNames(captureReadinessStateType)
        is [
            "UnknownTarget",
            "NotSummary",
            "TransitionInProgress",
            "RevealNotStarted",
            "RevealInProgress",
            "Ready",
            "DetectionFailed",
        ],
    "Capture readiness should distinguish unknown pages and displays that have not started."
);

var repositoryRoot = FindRepositoryRoot();
var controllerSource = File.ReadAllText(
    Path.Combine(
        repositoryRoot,
        "src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs"
    )
);
var screenshotServiceSource = File.ReadAllText(
    Path.Combine(repositoryRoot, "src/BazaarPlusPlus/Game/Screenshots/ScreenshotService.cs")
);
var mouseBlockerSource = File.ReadAllText(
    Path.Combine(repositoryRoot, "src/BazaarPlusPlus/Game/Screenshots/EndOfRunMouseBlocker.cs")
);
var inputCaptureSinkSource = File.ReadAllText(
    Path.Combine(repositoryRoot, "src/BazaarPlusPlus/Game/Screenshots/EndOfRunInputCaptureSink.cs")
);
var onEnableSource = ExtractSourceSegment(
    controllerSource,
    "private void OnEnable()",
    "private void OnDisable()"
);
var onDisableSource = ExtractSourceSegment(
    controllerSource,
    "private void OnDisable()",
    "private void OnDestroy()"
);
var endOfRunInitializingSource = ExtractSourceSegment(
    controllerSource,
    "private void OnEndOfRunScreenInitializing()",
    "private void OnRunInitializedObserved("
);
var updateSource = ExtractSourceSegment(
    controllerSource,
    "private void Update()",
    "private void OnRunStarted()"
);
var runStartedSource = ExtractSourceSegment(
    controllerSource,
    "private void OnRunStarted()",
    "private void OnEndOfRunScreenInitializing()"
);
var shouldBlockSource = ExtractSourceSegment(
    controllerSource,
    "private bool ShouldBlockContinueUntilCaptureInternal(",
    "private void SyncEndOfRunCapture()"
);
var syncCaptureSource = ExtractSourceSegment(
    controllerSource,
    "private void SyncEndOfRunCapture()",
    "private IEnumerator CaptureEndOfRun("
);
var revealStartedSource = ExtractSourceSegment(
    controllerSource,
    "public static void NotifySummaryRevealStarted(",
    "private bool ShouldBlockContinueUntilCaptureInternal("
);
var captureCoroutineSource = ExtractSourceSegment(
    controllerSource,
    "private IEnumerator CaptureEndOfRun(",
    "private void HandleCaptureFailure("
);
var captureFailureSource = ExtractSourceSegment(
    controllerSource,
    "private void HandleCaptureFailure(",
    "private void MarkSummaryRevealStarted("
);
var markRevealStartedSource = ExtractSourceSegment(
    controllerSource,
    "private void MarkSummaryRevealStarted(",
    "private EndOfRunCaptureReadinessOutcome GetCaptureReadiness("
);
var getReadinessSource = ExtractSourceSegment(
    controllerSource,
    "private EndOfRunCaptureReadinessOutcome GetCaptureReadiness(",
    "private void FailOpenIfCurrent("
);
var resetCaptureSource = ExtractSourceSegment(
    controllerSource,
    "private void ResetCaptureUiState(",
    "private void EnsureCaptureArmed("
);
Assert(
    onEnableSource.Contains(
        "Events.EndOfRunScreenInitializing.AddListener",
        StringComparison.Ordinal
    )
        && onDisableSource.Contains(
            "Events.EndOfRunScreenInitializing.RemoveListener",
            StringComparison.Ordinal
        )
        && endOfRunInitializingSource.Contains("_gate.ArmForEndOfRun();", StringComparison.Ordinal),
    "The native end-of-run screen event should subscribe, arm the gate, and unsubscribe."
);
Assert(
    !mouseBlockerSource.Contains("_inputSink?.", StringComparison.Ordinal)
        && !mouseBlockerSource.Contains("_blockerCanvasObject?.", StringComparison.Ordinal)
        && !mouseBlockerSource.Contains("_blockerObject?.", StringComparison.Ordinal),
    "Retained Unity blocker objects must use Unity destroyed-object checks, not CLR null-conditional calls."
);
Assert(
    ExtractSourceSegment(
            inputCaptureSinkSource,
            "public void CaptureFocus()",
            "public void ReleaseFocus()"
        )
        .Contains("if (this == null)", StringComparison.Ordinal)
        && ExtractSourceSegment(
                inputCaptureSinkSource,
                "public void ReleaseFocus()",
                "public void OnPointerClick("
            )
            .Contains("if (this == null)", StringComparison.Ordinal),
    "The input sink should tolerate calls after Unity destroys its native component."
);
Assert(
    updateSource.Contains("SyncEndOfRunCapture();", StringComparison.Ordinal)
        && syncCaptureSource.Contains("_gate.TryBeginAutomaticCapture(", StringComparison.Ordinal),
    "Update should route automatic capture through the reveal-aware gate."
);
Assert(
    runStartedSource.Contains("_gate.ResetForNewRun();", StringComparison.Ordinal)
        && onEnableSource.Contains("_current = this;", StringComparison.Ordinal)
        && onDisableSource.Contains("_current = null;", StringComparison.Ordinal),
    "The controller should reset between runs and expose static entry points only while enabled."
);
Assert(
    revealStartedSource.Contains(
        "current.MarkSummaryRevealStarted(summaryController);",
        StringComparison.Ordinal
    )
        && markRevealStartedSource.Contains(
            "_gate.MarkSummaryRevealStarted();",
            StringComparison.Ordinal
        )
        && getReadinessSource.Contains("_gate.HasSummaryRevealStarted()", StringComparison.Ordinal),
    "The native DisplayCardsAsync patch signal should reach the gate latch."
);
Assert(
    captureCoroutineSource.Contains(
        "Time.realtimeSinceStartup >= attemptDeadline",
        StringComparison.Ordinal
    )
        && captureCoroutineSource.Contains(
            "IsCaptureContextCurrent(screenController, captureGeneration)",
            StringComparison.Ordinal
        )
        && captureCoroutineSource.Contains(
            "Time.realtimeSinceStartup < persistenceDeadline",
            StringComparison.Ordinal
        )
        && captureCoroutineSource.Contains(
            "AbandonCaptureTask(captureTask, operation.ScreenshotId);",
            StringComparison.Ordinal
        )
        && captureCoroutineSource.Contains(
            "_gate.CompleteCaptureAttempt();",
            StringComparison.Ordinal
        )
        && captureCoroutineSource.Contains(
            "_activeCaptureTask = captureTask;",
            StringComparison.Ordinal
        )
        && resetCaptureSource.Contains("AbandonActiveCaptureTask();", StringComparison.Ordinal),
    "Capture and metadata polling need hard deadlines, context guards, and late-file cleanup."
);
Assert(
    shouldBlockSource.Contains("Time.unscaledTime,", StringComparison.Ordinal)
        && CountOccurrences(syncCaptureSource, "Time.unscaledTime,") >= 2
        && captureFailureSource.Contains(
            "Time.unscaledTime + CaptureRetryCooldownSeconds",
            StringComparison.Ordinal
        ),
    "Reveal fallback and retry deadlines should remain bounded even when native game time is paused."
);
Assert(
    !controllerSource.Contains("FirstCaptureDelaySeconds", StringComparison.Ordinal),
    "The production controller must not fall back to a fixed first-capture delay."
);
Assert(
    !controllerSource.Contains("TryCaptureFirstContinue", StringComparison.Ordinal),
    "Continue clicks must not remain the trigger for end-of-run capture."
);
Assert(
    !controllerSource.Contains("CaptureState action=armed", StringComparison.Ordinal)
        && !controllerSource.Contains(
            "CaptureState action=controller-tracked",
            StringComparison.Ordinal
        )
        && !controllerSource.Contains(
            "CaptureState action=reveal-started",
            StringComparison.Ordinal
        )
        && !controllerSource.Contains("BlockerState", StringComparison.Ordinal)
        && !controllerSource.Contains("CaptureState action=captured", StringComparison.Ordinal)
        && !controllerSource.Contains("BppLog.", StringComparison.Ordinal)
        && !screenshotServiceSource.Contains("BppLog.", StringComparison.Ordinal)
        && syncCaptureSource.Contains("EnsureCaptureOperation(readiness)", StringComparison.Ordinal)
        && captureCoroutineSource.Contains(
            "ScreenshotId = operation.ScreenshotId",
            StringComparison.Ordinal
        ),
    "Capture terminal ownership should live in the preallocated operation, not controller/service prose logs."
);
Assert(
    syncCaptureSource.IndexOf("EnsureCaptureOperation(readiness)", StringComparison.Ordinal)
        < syncCaptureSource.IndexOf("TryBeginAutomaticCapture(", StringComparison.Ordinal)
        && syncCaptureSource.Contains(
            "CompleteReadinessFailureIfFinished(readinessOutcome)",
            StringComparison.Ordinal
        ),
    "Readiness failures must allocate and close the screenshot operation before the gate fails open."
);
Assert(
    captureCoroutineSource.IndexOf(
        "RecordVerifiedArtifact(capture.FilePath)",
        StringComparison.Ordinal
    ) < captureCoroutineSource.IndexOf("_persistAsync?.Invoke", StringComparison.Ordinal)
        && resetCaptureSource.Contains("TryCompleteContextReset", StringComparison.Ordinal),
    "A verified PNG must be recorded before metadata wait so context reset degrades it instead of reporting it missing."
);
var continuePatchSource = File.ReadAllText(
    Path.Combine(repositoryRoot, "src/BazaarPlusPlus/Patches/EndOfRun/EndOfRunScreenshotPatch.cs")
);
var revealPatchSource = File.ReadAllText(
    Path.Combine(
        repositoryRoot,
        "src/BazaarPlusPlus/Patches/EndOfRun/EndOfRunSummaryRevealPatch.cs"
    )
);
var rawRevealCompletionPatchSource = File.ReadAllText(
    Path.Combine(
        repositoryRoot,
        "src/BazaarPlusPlus/Patches/EndOfRun/EndOfRunRawRevealCompletionPatch.cs"
    )
);
Assert(
    continuePatchSource.Contains(
        "[HarmonyPatch(typeof(EndOfRunScreenController), \"OnContinueClick\")]",
        StringComparison.Ordinal
    )
        && continuePatchSource.Contains("[HarmonyPrefix]", StringComparison.Ordinal)
        && ExtractSourceSegment(continuePatchSource, "private static bool Prefix(", "    }")
            .Contains(
                "return !EndOfRunScreenshotController.ShouldBlockContinueUntilCapture(__instance);",
                StringComparison.Ordinal
            ),
    "The Harmony Continue prefix should remain a safety guard for the capture gate."
);
Assert(
    revealPatchSource.Contains(
        "[HarmonyPatch(typeof(EndOfRunSummaryController), \"DisplayCardsAsync\")]",
        StringComparison.Ordinal
    )
        && revealPatchSource.Contains("[HarmonyPrefix]", StringComparison.Ordinal)
        && ExtractSourceSegment(revealPatchSource, "private static void Prefix(", "    }")
            .Contains("NotifySummaryRevealStarted(__instance);", StringComparison.Ordinal),
    "DisplayCardsAsync should mark native display start through its Harmony prefix."
);
Assert(
    rawRevealCompletionPatchSource.Contains(
        "[HarmonyPatch(typeof(BaseCardRevealAnimationDriver), \"CreateRawGraph\")]",
        StringComparison.Ordinal
    )
        && rawRevealCompletionPatchSource.Contains(
            "ScriptPlayableOutput.Create(",
            StringComparison.Ordinal
        )
        && rawRevealCompletionPatchSource.Contains("__result,", StringComparison.Ordinal)
        && rawRevealCompletionPatchSource.Contains(
            "completionOutput.SetSourcePlayable(root, 1);",
            StringComparison.Ordinal
        ),
    "Raw end-of-run graphs need a ScriptPlayableOutput so DelayPlayableBehavior.ProcessFrame can mark roots done."
);

Console.WriteLine("End-of-run screenshot gate checks passed.");

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current != null)
    {
        if (
            File.Exists(
                Path.Combine(
                    current.FullName,
                    "src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs"
                )
            )
        )
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not locate the repository root.");
}

static string ExtractSourceSegment(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    if (start < 0)
        throw new InvalidOperationException($"Source marker not found: {startMarker}");

    var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
    if (end < 0)
        throw new InvalidOperationException(
            $"Source marker not found after {startMarker}: {endMarker}"
        );

    return source[start..end];
}

static int CountOccurrences(string source, string value)
{
    var count = 0;
    var index = 0;
    while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }

    return count;
}

static void InvokeArmForEndOfRun(Type type, object instance)
{
    var method = type.GetMethod("ArmForEndOfRun", BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.ArmForEndOfRun");

    method.Invoke(instance, []);
}

static bool InvokeTryBeginAutomaticCapture(
    Type type,
    object instance,
    object readiness,
    bool isCaptureEnabled,
    float nowSeconds,
    float fallbackTimeoutSeconds
)
{
    var method = type.GetMethod(
        "TryBeginAutomaticCapture",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.TryBeginAutomaticCapture"
        );

    return (bool)(
        method.Invoke(instance, [readiness, isCaptureEnabled, nowSeconds, fallbackTimeoutSeconds])
        ?? false
    );
}

static bool InvokeShouldBlockContinue(
    Type type,
    object instance,
    object readiness,
    bool isCaptureEnabled,
    float nowSeconds,
    float fallbackTimeoutSeconds
)
{
    var method = type.GetMethod("ShouldBlockContinue", BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.ShouldBlockContinue"
        );

    return (bool)(
        method.Invoke(instance, [readiness, isCaptureEnabled, nowSeconds, fallbackTimeoutSeconds])
        ?? false
    );
}

static object InvokeGetCaptureReadiness(
    Type detectorType,
    object screenController,
    bool hasSummaryRevealStarted
)
{
    var method = detectorType.GetMethod("GetState", BindingFlags.Public | BindingFlags.Static);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {detectorType.FullName}.GetState");

    return method.Invoke(null, [screenController, hasSummaryRevealStarted])
        ?? throw new InvalidOperationException("GetState returned null.");
}

static string GetEnumName(Type enumType, object value)
{
    return Enum.GetName(enumType, value)
        ?? throw new InvalidOperationException($"No enum name found for {enumType.FullName}.");
}

static void InvokeMarkSummaryRevealStarted(Type type, object instance)
{
    var method = type.GetMethod(
        "MarkSummaryRevealStarted",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.MarkSummaryRevealStarted"
        );

    method.Invoke(instance, []);
}

static bool InvokeHasSummaryRevealStarted(Type type, object instance)
{
    var method = type.GetMethod(
        "HasSummaryRevealStarted",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.HasSummaryRevealStarted"
        );

    return (bool)(method.Invoke(instance, []) ?? false);
}

static bool InvokeHasFinishedForCurrentRun(Type type, object instance)
{
    var method = type.GetMethod(
        "HasFinishedForCurrentRun",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.HasFinishedForCurrentRun"
        );

    return (bool)(method.Invoke(instance, []) ?? false);
}

static void InvokeResetForNewRun(Type type, object instance)
{
    var method = type.GetMethod("ResetForNewRun", BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.ResetForNewRun");

    method.Invoke(instance, []);
}

static bool InvokeAbortCaptureAttempt(Type type, object instance, float retryAvailableAtSeconds)
{
    var method = type.GetMethod("AbortCaptureAttempt", BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.AbortCaptureAttempt"
        );
    }

    return (bool)(method.Invoke(instance, [retryAvailableAtSeconds]) ?? false);
}

static void InvokeCompleteCaptureAttempt(Type type, object instance)
{
    var method = type.GetMethod(
        "CompleteCaptureAttempt",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.CompleteCaptureAttempt"
        );
    }

    method.Invoke(instance, []);
}

static bool InvokeIsAttemptInFlight(Type type, object instance)
{
    var method = type.GetMethod(
        "IsCaptureAttemptInFlight",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.IsCaptureAttemptInFlight"
        );
    }

    return (bool)(method.Invoke(instance, []) ?? false);
}

static string InvokeBuildRelativePath(Type type, string? runId, DateTimeOffset capturedAtLocal)
{
    var method = type.GetMethod(
        "BuildRelativePath",
        BindingFlags.Public | BindingFlags.Static,
        [typeof(string), typeof(DateTimeOffset)]
    );
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.BuildRelativePath");

    return (string?)method.Invoke(null, [runId, capturedAtLocal])
        ?? throw new InvalidOperationException("BuildRelativePath returned null.");
}

static string InvokeGetSummaryRevealState(
    Type detectorType,
    Type stateType,
    object screenController
)
{
    var method = detectorType.GetMethod(
        "GetRevealState",
        BindingFlags.Public | BindingFlags.Static
    );
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {detectorType.FullName}.GetRevealState"
        );
    }

    var value =
        method.Invoke(null, [screenController])
        ?? throw new InvalidOperationException("GetRevealState returned null.");
    return Enum.GetName(stateType, value)
        ?? throw new InvalidOperationException("Reveal state enum name was null.");
}

static void SetProperty(Type type, object instance, string name, object? value)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    property.SetValue(instance, value);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

namespace TheBazaar.UI.EndOfRun
{
    public sealed class EndOfRunScreenController
    {
        private object? _activeController;
        private int _transitionCount;

        public EndOfRunScreenController(object? activeController)
            : this(0, activeController) { }

        public EndOfRunScreenController(int transitionCount, object? activeController)
        {
            _transitionCount = transitionCount;
            _activeController = activeController;
        }

        public void SetTransitionCount(int transitionCount)
        {
            _transitionCount = transitionCount;
        }
    }

    public sealed class EndOfRunSummaryController
    {
        private object?[] loadedCards;
        private object? _skillSequence;

        public EndOfRunSummaryController(params object?[] loadedCards)
        {
            this.loadedCards = loadedCards;
            _skillSequence = new FakeSequence(duration: 0f, isComplete: false);
        }

        public void SetLoadedCards(params object?[] cards)
        {
            loadedCards = cards;
        }

        public void SetSkillSequence(object? skillSequence)
        {
            _skillSequence = skillSequence;
        }
    }

    public sealed class OtherEndOfRunController;

    public sealed class FakeItemController
    {
        public FakeAnimator Animator;

        public FakeItemController(bool faceUp)
        {
            Animator = new FakeAnimator(faceUp);
        }
    }

    public sealed class FakeFieldItemController
    {
        public FakeAnimator Animator;

        public FakeFieldItemController(bool faceUp)
        {
            Animator = new FakeAnimator(faceUp);
        }
    }

    public sealed class BadItemController;

    public sealed class ThrowingItemController
    {
        public ThrowingAnimator Animator = new();
    }

    public sealed class ThrowingAnimator
    {
        public bool GetBool(string parameterName)
        {
            throw new InvalidOperationException($"Cannot read {parameterName}.");
        }
    }

    public class FakeTween
    {
        public float duration;
        public bool isComplete;

        protected FakeTween(float duration, bool isComplete)
        {
            this.duration = duration;
            this.isComplete = isComplete;
        }
    }

    public sealed class FakeSequence : FakeTween
    {
        public FakeSequence(float duration, bool isComplete)
            : base(duration, isComplete) { }

        public void SetComplete(bool value)
        {
            isComplete = value;
        }
    }

    public sealed class FakeAnimator
    {
        private bool _faceUp;

        public FakeAnimator(bool faceUp)
        {
            _faceUp = faceUp;
        }

        public bool GetBool(string parameterName)
        {
            return parameterName == "FaceUp" && _faceUp;
        }

        public void SetFaceUp(bool faceUp)
        {
            _faceUp = faceUp;
        }
    }
}
