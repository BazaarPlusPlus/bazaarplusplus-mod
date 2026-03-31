#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;

var assembly = Assembly.Load("BazaarPlusPlus");
var eventBusType = RequireType("BazaarPlusPlus.Core.Events.InMemoryBppEventBus");
var coreType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingControllerCore");
var moduleType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingModule");
var runtimeHostType = RequireType("BazaarPlusPlus.Core.Runtime.BppRuntimeHost");
var runLoggingSyncRequestedType = RequireType("BazaarPlusPlus.Core.Events.RunLoggingSyncRequested");
var captureServiceType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogCaptureService");
var inferenceServiceType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogInferenceService");
var runLifecycleChangedType = RequireType("BazaarPlusPlus.Core.Events.RunLifecycleChanged");
var runExitKindType = RequireType("BazaarPlusPlus.Core.RunContext.RunExitKind");

var store = new FakeRunLogStore();
var now = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
var manager = new RunLogSessionManager(store, () => now);
var request = new RunLogCreateRequest
{
    SchemaVersion = 1,
    RunId = "run-module-test-1",
    StartedAtUtc = now,
    Hero = "Vanessa",
    GameMode = "Ranked",
    Day = 1,
    Hour = 1,
};
manager.EnsureActiveSession(request);

var eventBus =
    Activator.CreateInstance(eventBusType)
    ?? throw new InvalidOperationException("Failed to construct InMemoryBppEventBus.");
var captureService =
    Activator.CreateInstance(captureServiceType)
    ?? throw new InvalidOperationException("Failed to construct RunLogCaptureService.");
var inferenceService =
    Activator.CreateInstance(inferenceServiceType)
    ?? throw new InvalidOperationException("Failed to construct RunLogInferenceService.");
var core =
    Activator.CreateInstance(coreType, [manager, captureService])
    ?? throw new InvalidOperationException("Failed to construct RunLoggingControllerCore.");
var module =
    Activator.CreateInstance(
        moduleType,
        [
            eventBus,
            manager,
            core,
            inferenceService,
            new Func<bool>(() => false),
            new Func<RunLogSessionState?>(() => null),
            new Func<DateTime>(() => now.UtcDateTime),
            new Func<string, RunLogCompletion>(reason => new RunLogCompletion
            {
                SchemaVersion = 1,
                Status = "completed",
                EndedAtUtc = now,
                Reason = reason,
            }),
        ]
    )
    ?? throw new InvalidOperationException("Failed to construct RunLoggingModule.");

var runContext = runtimeHostType.GetProperty("RunContext", BindingFlags.Public | BindingFlags.Static)!
    .GetValue(null)!;

SetProperty(runContext, "CurrentServerRunId", request.RunId);
SetProperty(runContext, "IsInGameRun", false);
InvokeVoid(moduleType, module, "OnRunLoggingSyncRequested", [Activator.CreateInstance(runLoggingSyncRequestedType)!]);

Assert(store.CompleteRunCalls == 0, "Transient run exit should not complete the run immediately.");
Assert(
    GetField(module, "_deferredRunCompletion") == null,
    "Transient run exit should not create deferred completion without an explicit terminal signal."
);

SetProperty(runContext, "IsInGameRun", true);
InvokeVoid(moduleType, module, "OnRunLoggingSyncRequested", [Activator.CreateInstance(runLoggingSyncRequestedType)!]);

Assert(
    GetField(module, "_deferredRunCompletion") == null,
    "Resuming the same run should keep deferred completion empty."
);
Assert(store.CompleteRunCalls == 0, "Resuming the same run should not complete the run.");

var completedExit = Activator.CreateInstance(runLifecycleChangedType)!;
SetProperty(completedExit, "IsInGameRun", false);
SetProperty(completedExit, "LastRunExitKind", Enum.Parse(runExitKindType, "Completed"));
SetProperty(completedExit, "Reason", "Run ended");
InvokeVoid(moduleType, module, "OnRunLifecycleChanged", [completedExit]);

Assert(
    GetField(module, "_deferredRunCompletion") != null,
    "Explicit run end should create deferred completion."
);

SetProperty(runContext, "IsInGameRun", false);
InvokeVoid(moduleType, module, "OnRunLoggingSyncRequested", [Activator.CreateInstance(runLoggingSyncRequestedType)!]);

Assert(store.CompleteRunCalls == 1, "An explicit run end should complete the run on sync.");

Console.WriteLine("RunLogging module checks passed.");

static Type RequireType(string fullName)
{
    return Assembly.Load("BazaarPlusPlus").GetType(fullName, throwOnError: true)!;
}

static void InvokeVoid(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    method.Invoke(instance, args);
}

static object? GetField(object instance, string name)
{
    var field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    if (field == null)
        throw new InvalidOperationException($"Field not found: {instance.GetType().FullName}.{name}");

    return field.GetValue(instance);
}

static void SetProperty(object instance, string name, object? value)
{
    var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException(
            $"Property not found: {instance.GetType().FullName}.{name}"
        );

    property.SetValue(instance, value);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

file sealed class FakeRunLogStore : IRunLogStore
{
    public int CompleteRunCalls { get; private set; }

    public RunLogSessionState? ActiveState { get; private set; }

    public RunLogSessionState? TryResumeActiveRun()
    {
        return ActiveState;
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        ActiveState = new RunLogSessionState
        {
            RunId = request.RunId,
            SchemaVersion = request.SchemaVersion,
            StartedAtUtc = request.StartedAtUtc,
            LastSeenAtUtc = request.StartedAtUtc,
            LastSeq = 0,
            Day = request.Day,
            Hour = request.Hour,
        };
        return ActiveState;
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        if (ActiveState == null)
            return;

        ActiveState.LastSeq = entry.Seq;
        ActiveState.LastSeenAtUtc = entry.Ts;
        ActiveState.Day = entry.Day;
        ActiveState.Hour = entry.Hour;
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        if (ActiveState == null)
            return;

        ActiveState.LastSeq = checkpoint.LastSeq;
        ActiveState.LastSeenAtUtc = checkpoint.LastSeenAtUtc;
        ActiveState.Day = checkpoint.Day;
        ActiveState.Hour = checkpoint.Hour;
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        CompleteRunCalls++;
        ActiveState = null;
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        ActiveState = null;
    }
}
