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
    !InvokeShouldCaptureOnContinue(gateType, gate, isInteractionBlocked: true, nowSeconds: 0f),
    "Blocked continue interactions should not trigger a screenshot."
);

Assert(
    InvokeShouldCaptureOnContinue(gateType, gate, isInteractionBlocked: false, nowSeconds: 0f),
    "The first available continue interaction should arm a screenshot attempt."
);

Assert(
    !InvokeShouldCaptureOnContinue(gateType, gate, isInteractionBlocked: false, nowSeconds: 0f),
    "Only one capture attempt should be in flight at a time."
);

InvokeAbortCaptureAttempt(gateType, gate, retryAvailableAtSeconds: 5f);

Assert(
    !InvokeShouldCaptureOnContinue(gateType, gate, isInteractionBlocked: false, nowSeconds: 4.99f),
    "A failed capture attempt should stay throttled until the retry window opens."
);

Assert(
    InvokeShouldCaptureOnContinue(gateType, gate, isInteractionBlocked: false, nowSeconds: 5f),
    "A failed capture attempt should re-open the screenshot opportunity."
);

InvokeCompleteCaptureAttempt(gateType, gate);

Assert(
    InvokeIsAttemptInFlight(gateType, gate),
    "Completed screenshot attempts should keep continue suppressed until the queued passthrough executes."
);

Assert(
    !InvokeShouldCaptureOnContinue(gateType, gate, isInteractionBlocked: false, nowSeconds: 10f),
    "A completed screenshot should stay consumed for the rest of the run."
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

InvokeResetForNewRun(gateType, gate);

Assert(
    InvokeShouldCaptureOnContinue(gateType, gate, isInteractionBlocked: false, nowSeconds: 0f),
    "Starting a new run should re-arm the screenshot gate."
);

Assert(
    !InvokeShouldCaptureOnContinue(gateType, gate, isInteractionBlocked: false, nowSeconds: 0f),
    "Only one capture attempt should be in flight after re-arming for a new run."
);

var pathBuilderType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.ScreenshotPathBuilder",
    throwOnError: true
)!;
var summaryRevealDetectorType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.EndOfRunSummaryRevealDetector",
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
    InvokeShouldAllowContinue(
        continueStateEvaluatorType,
        new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
            new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(),
            transitionCount: 1
        ),
        suppressWhileCaptureInFlight: false
    ),
    "Continue should no longer depend on the game's end-of-run transition state once the time gate has opened."
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
    "Mouse blocking should clear once reveal is complete and no transition is active."
);
Console.WriteLine("End-of-run screenshot gate checks passed.");

static bool InvokeShouldCaptureOnContinue(
    Type type,
    object instance,
    bool isInteractionBlocked,
    float nowSeconds
)
{
    var method = type.GetMethod(
        "ShouldCaptureOnContinue",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.ShouldCaptureOnContinue"
        );

    return (bool)(method.Invoke(instance, [isInteractionBlocked, nowSeconds]) ?? false);
}

static void InvokeResetForNewRun(Type type, object instance)
{
    var method = type.GetMethod("ResetForNewRun", BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.ResetForNewRun");

    method.Invoke(instance, []);
}

static void InvokeAbortCaptureAttempt(Type type, object instance, float retryAvailableAtSeconds)
{
    var method = type.GetMethod("AbortCaptureAttempt", BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
    {
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.AbortCaptureAttempt"
        );
    }

    method.Invoke(instance, [retryAvailableAtSeconds]);
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

    public sealed class FakeFieldItemController
    {
        public FakeAnimator Animator;

        public FakeFieldItemController(bool faceUp)
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
}
