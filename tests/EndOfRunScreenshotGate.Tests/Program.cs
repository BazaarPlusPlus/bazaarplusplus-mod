#nullable enable
using System.IO;
using System.Reflection;

var assembly = Assembly.Load("BazaarPlusPlus");
var gateType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunScreenshotGate",
    throwOnError: true
)!;
var gate =
    Activator.CreateInstance(gateType)
    ?? throw new InvalidOperationException("Failed to construct EndOfRunScreenshotGate.");

Assert(
    !InvokeShouldCapture(gateType, gate, isInteractionBlocked: true),
    "Blocked continue interactions should not trigger a screenshot."
);

Assert(
    InvokeShouldCapture(gateType, gate, isInteractionBlocked: false),
    "The first available continue interaction should arm a screenshot attempt."
);

Assert(
    !InvokeShouldCapture(gateType, gate, isInteractionBlocked: false),
    "Only one in-flight screenshot attempt should be allowed at a time."
);

InvokeMarkAttemptAborted(gateType, gate);

Assert(
    InvokeShouldCapture(gateType, gate, isInteractionBlocked: false),
    "Aborted screenshot attempts should re-open the capture opportunity."
);

Assert(
    !InvokeShouldCapture(gateType, gate, isInteractionBlocked: false),
    "Only one in-flight screenshot attempt should be allowed after re-arming."
);

InvokeMarkAttemptCompleted(gateType, gate);

Assert(
    InvokeIsAttemptInFlight(gateType, gate),
    "Completed screenshot attempts should keep continue suppressed until the queued passthrough executes."
);

Assert(
    !InvokeShouldCapture(gateType, gate, isInteractionBlocked: false),
    "Completed screenshot attempts should permanently consume this run's capture."
);

InvokeResetForNewRun(gateType, gate);

Assert(
    InvokeShouldCapture(gateType, gate, isInteractionBlocked: false),
    "Starting a new run should re-arm the screenshot gate."
);

InvokeAllowNextPassthrough(gateType, gate);
Assert(
    InvokeConsumePassthrough(gateType, gate),
    "Allowing passthrough should permit exactly one follow-up continue invocation."
);
Assert(
    !InvokeConsumePassthrough(gateType, gate),
    "Passthrough should be consumed after one invocation."
);
Assert(
    !InvokeIsAttemptInFlight(gateType, gate),
    "Consuming passthrough should clear the in-flight suppression state."
);

var pathBuilderType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.ScreenshotPathBuilder",
    throwOnError: true
)!;
var summaryRevealDetectorType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunSummaryRevealDetector",
    throwOnError: true
)!;
var continueButtonFeedbackType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunContinueButtonFeedback",
    throwOnError: true
)!;
var continueStateEvaluatorType = RequireType(
    assembly,
    "BazaarPlusPlus.Game.Screenshots.EndOfRunContinueStateEvaluator"
);
var summaryRevealStateType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunSummaryRevealState",
    throwOnError: true
)!;
var captureSourceType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.RunScreenshotCaptureSource",
    throwOnError: true
)!;
Assert(
    Enum.GetNames(captureSourceType) is ["EndOfRunAuto"],
    "Screenshot capture sources should only expose EndOfRunAuto."
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
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController()
        )
    ) == "RevealComplete",
    "Empty summary boards should not stay blocked."
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
        is ["NotSummary", "RevealInProgress", "RevealComplete", "DetectionFailed"],
    "Summary reveal state enum should expose the expected states."
);

Assert(
    !InvokeShouldAllowContinue(
        continueStateEvaluatorType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(),
            transitionCount: 1
        ),
        suppressWhileCaptureInFlight: false
    ),
    "Continue should stay disabled while the game reports an end-of-run transition in progress."
);
Assert(
    !InvokeShouldAllowContinue(
        continueStateEvaluatorType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
                new TheBazaar.UI.EndOfRun.FakeItemController(false)
            )
        ),
        suppressWhileCaptureInFlight: false
    ),
    "Continue should stay disabled while summary cards are still revealing."
);
Assert(
    !InvokeShouldAllowContinue(
        continueStateEvaluatorType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
                new TheBazaar.UI.EndOfRun.FakeItemController(true)
            )
        ),
        suppressWhileCaptureInFlight: true
    ),
    "Continue should stay disabled while an automatic screenshot is still suppressing input."
);
Assert(
    InvokeShouldAllowContinue(
        continueStateEvaluatorType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
                new TheBazaar.UI.EndOfRun.FakeItemController(true)
            )
        ),
        suppressWhileCaptureInFlight: false
    ),
    "Continue should re-enable once reveal is complete, no transition is active, and capture suppression is cleared."
);

var screenWithButton = new TheBazaar.UI.EndOfRun.ScreenWithContinueButton();
InvokeSyncContinueInteractable(continueButtonFeedbackType, screenWithButton, shouldAllowContinue: false);
Assert(
    screenWithButton.ContinueButton.SetUnInteractableCount == 1
        && screenWithButton.ContinueButton.SetInteractableCount == 0,
    "Blocking summary reveal should disable the continue button."
);
InvokeSyncContinueInteractable(continueButtonFeedbackType, screenWithButton, shouldAllowContinue: true);
Assert(
    screenWithButton.ContinueButton.SetInteractableCount == 1,
    "Resolved summary reveal should re-enable the continue button."
);

Console.WriteLine("End-of-run screenshot gate checks passed.");

static bool InvokeShouldCapture(Type type, object instance, bool isInteractionBlocked)
{
    var method = type.GetMethod(
        "ShouldCaptureOnContinue",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.ShouldCaptureOnContinue"
        );

    return (bool)(method.Invoke(instance, [isInteractionBlocked]) ?? false);
}

static void InvokeResetForNewRun(Type type, object instance)
{
    var method = type.GetMethod("ResetForNewRun", BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.ResetForNewRun");

    method.Invoke(instance, []);
}

static void InvokeMarkAttemptAborted(Type type, object instance)
{
    var method = type.GetMethod("MarkAttemptAborted", BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.MarkAttemptAborted"
        );

    method.Invoke(instance, []);
}

static void InvokeMarkAttemptCompleted(Type type, object instance)
{
    var method = type.GetMethod(
        "MarkAttemptCompleted",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.MarkAttemptCompleted"
        );
    }

    method.Invoke(instance, []);
}

static bool InvokeIsAttemptInFlight(Type type, object instance)
{
    var method = type.GetMethod("IsAttemptInFlight", BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
    {
        throw new InvalidOperationException($"Method not found: {type.FullName}.IsAttemptInFlight");
    }

    return (bool)(method.Invoke(instance, []) ?? false);
}

static void InvokeAllowNextPassthrough(Type type, object instance)
{
    var method = type.GetMethod(
        "AllowNextContinuePassthrough",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.AllowNextContinuePassthrough"
        );
    }

    method.Invoke(instance, []);
}

static bool InvokeConsumePassthrough(Type type, object instance)
{
    var method = type.GetMethod(
        "ConsumeContinuePassthrough",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.ConsumeContinuePassthrough"
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

static string InvokeGetSummaryRevealState(Type detectorType, Type stateType, object screenController)
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

    var value = method.Invoke(null, [screenController])
        ?? throw new InvalidOperationException("GetRevealState returned null.");
    return Enum.GetName(stateType, value)
        ?? throw new InvalidOperationException("Reveal state enum name was null.");
}

static void InvokeSyncContinueInteractable(Type type, object screenController, bool shouldAllowContinue)
{
    var method = type.GetMethod(
        "SyncInteractivity",
        BindingFlags.Public | BindingFlags.Static
    );
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.SyncInteractivity"
        );
    }

    method.Invoke(null, [screenController, shouldAllowContinue]);
}

static bool InvokeShouldAllowContinue(
    Type type,
    object screenController,
    bool suppressWhileCaptureInFlight
)
{
    var method = type.GetMethod("ShouldAllowContinue", BindingFlags.Public | BindingFlags.Static);
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.ShouldAllowContinue"
        );
    }

    return (bool)(method.Invoke(null, [screenController, suppressWhileCaptureInFlight]) ?? false);
}

static Type RequireType(Assembly assembly, string fullName)
{
    return assembly.GetType(fullName, throwOnError: true)
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
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
        private readonly object? _activeController;
        private readonly int _transitionCount;

        public EndOfRunScreenController(object? activeController, int transitionCount = 0)
        {
            _activeController = activeController;
            _transitionCount = transitionCount;
        }
    }

    public sealed class EndOfRunSummaryController
    {
        private readonly object?[] loadedCards;

        public EndOfRunSummaryController(params object?[] loadedCards)
        {
            this.loadedCards = loadedCards;
        }
    }

    public sealed class OtherEndOfRunController;

    public sealed class FakeItemController
    {
        public FakeAnimator Animator { get; }

        public FakeItemController(bool faceUp)
        {
            Animator = new FakeAnimator(faceUp);
        }
    }

    public sealed class BadItemController;

    public sealed class FakeAnimator
    {
        private readonly bool _faceUp;

        public FakeAnimator(bool faceUp)
        {
            _faceUp = faceUp;
        }

        public bool GetBool(string parameterName)
        {
            return parameterName == "FaceUp" && _faceUp;
        }
    }

    public sealed class ScreenWithContinueButton
    {
        private readonly FakeContinueButton continueButton = new();

        public FakeContinueButton ContinueButton => continueButton;
    }

    public sealed class FakeContinueButton
    {
        public int SetInteractableCount { get; private set; }

        public int SetUnInteractableCount { get; private set; }

        public void SetInteractable()
        {
            SetInteractableCount++;
        }

        public void SetUnInteractable()
        {
            SetUnInteractableCount++;
        }
    }
}
