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

var pathBuilderType = assembly.GetType(
    "BazaarPlusPlus.Game.Screenshots.ScreenshotPathBuilder",
    throwOnError: true
)!;
var screenshotPath = InvokeBuildRelativePath(
    pathBuilderType,
    runId: "Run-42/Final",
    capturedAtLocal: new DateTimeOffset(2026, 4, 7, 21, 30, 15, TimeSpan.FromHours(8))
);
Assert(
    screenshotPath == Path.Combine("2026-04-07", "run-42final-213015000.png"),
    $"Unexpected screenshot path: {screenshotPath}"
);

var fallbackPath = InvokeBuildRelativePath(
    pathBuilderType,
    runId: null,
    capturedAtLocal: new DateTimeOffset(2026, 4, 7, 9, 5, 4, TimeSpan.FromHours(-7))
);
Assert(
    fallbackPath == Path.Combine("2026-04-07", "anonymous-090504000.png"),
    $"Expected anonymous fallback path, got: {fallbackPath}"
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
    var method = type.GetMethod(
        "MarkAttemptAborted",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.MarkAttemptAborted");

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
        BindingFlags.Public | BindingFlags.Static
    );
    if (method == null)
        throw new InvalidOperationException(
            $"Method not found: {type.FullName}.BuildRelativePath"
        );

    return (string?)method.Invoke(null, [runId, capturedAtLocal])
        ?? throw new InvalidOperationException("BuildRelativePath returned null.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
