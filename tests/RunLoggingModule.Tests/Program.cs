#nullable enable
using System.Reflection;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Storage.RunLog;
using BepInEx.Logging;

var assembly = Assembly.Load("BazaarPlusPlus");
var eventBusType = RequireType("BazaarPlusPlus.Core.Events.InMemoryBppEventBus");
var coreType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingControllerCore");
var moduleType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingModule");
var combatReplayPersistenceDrainedType = RequireType(
    "BazaarPlusPlus.Core.Events.CombatReplayPersistenceDrained"
);
var runInitializedObservedType = RequireType("BazaarPlusPlus.Core.Events.RunInitializedObserved");
var runContextStoreType = RequireType("BazaarPlusPlus.GameInterop.RunContextStore");
var captureServiceType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogCaptureService");
var runLifecycleChangedType = RequireType("BazaarPlusPlus.Core.Events.RunLifecycleChanged");
var runExitKindType = RequireType("BazaarPlusPlus.Core.RunContext.RunExitKind");
var pvpBattleRecordedType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleRecorded");
using var logSource = new ManualLogSource("run-logging-tests");
var capturedLogs = new List<LogEventArgs>();
logSource.LogEvent += (_, args) => capturedLogs.Add(args);
InstallBppLog(logSource);

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
var core =
    Activator.CreateInstance(coreType, [manager, captureService])
    ?? throw new InvalidOperationException("Failed to construct RunLoggingControllerCore.");
var runContext =
    Activator.CreateInstance(runContextStoreType)
    ?? throw new InvalidOperationException("Failed to construct RunContextStore.");
var pendingReplayPersistence = false;

RunLogCreateRequest EnsureActiveRunFromGame()
{
    var currentRunId = (string?)GetProperty(runContext.GetType(), runContext, "CurrentServerRunId");
    if (string.IsNullOrWhiteSpace(currentRunId))
        throw new InvalidOperationException("CurrentServerRunId should be set before activation.");

    return new RunLogCreateRequest
    {
        SchemaVersion = 1,
        RunId = currentRunId,
        StartedAtUtc = now,
        Hero = "Vanessa",
        GameMode = "Ranked",
        Day = 1,
        Hour = 1,
    };
}

RunLogSessionState? EnsureActiveSessionFromGame()
{
    var ensureRunStarted = coreType.GetMethod(
        "EnsureRunStarted",
        BindingFlags.Public | BindingFlags.Instance
    );
    if (ensureRunStarted == null)
        throw new InvalidOperationException(
            "RunLoggingControllerCore.EnsureRunStarted should exist."
        );

    return (RunLogSessionState?)ensureRunStarted.Invoke(core, [EnsureActiveRunFromGame()]);
}

var module =
    Activator.CreateInstance(
        moduleType,
        [
            eventBus,
            runContext,
            manager,
            core,
            new Func<bool>(() => pendingReplayPersistence),
            new Func<RunLogSessionState?>(EnsureActiveSessionFromGame),
            new Func<DateTime>(() => now.UtcDateTime),
            new Func<string, RunLogCompletion>(reason => new RunLogCompletion
            {
                SchemaVersion = 1,
                Status = "completed",
                EndedAtUtc = now,
                Reason = reason,
            }),
            new Func<string, RunLogAbandonment>(reason => new RunLogAbandonment
            {
                SchemaVersion = 1,
                Status = "abandoned",
                EndedAtUtc = now,
                Reason = reason,
            }),
        ]
    ) ?? throw new InvalidOperationException("Failed to construct RunLoggingModule.");

SetProperty(runContext, "CurrentServerRunId", request.RunId);
SetProperty(runContext, "IsInGameRun", false);

var interruptedExit = Activator.CreateInstance(runLifecycleChangedType)!;
SetProperty(interruptedExit, "IsInGameRun", false);
SetProperty(interruptedExit, "LastRunExitKind", Enum.Parse(runExitKindType, "Interrupted"));
SetProperty(interruptedExit, "Reason", "Run interrupted");
InvokeVoid(moduleType, module, "OnRunLifecycleChanged", [interruptedExit]);

Assert(store.MarkRunAbandonedCalls == 0, "Run interruption should not abandon immediately.");
Assert(
    Equals(GetField(module, "_pendingInterruptedRunId"), request.RunId),
    "Run interruption should enter interrupted-pending state for the active run."
);

SetProperty(runContext, "CurrentServerRunId", request.RunId);
SetProperty(runContext, "IsInGameRun", true);
var resumedRun = Activator.CreateInstance(runInitializedObservedType)!;
SetProperty(resumedRun, "RunId", request.RunId);
InvokeVoid(moduleType, module, "OnRunInitializedObserved", [resumedRun]);

Assert(
    GetField(module, "_pendingInterruptedRunId") == null,
    "Resuming the same run id should clear interrupted-pending state."
);
Assert(store.MarkRunAbandonedCalls == 0, "Resuming the same run should not abandon it.");

InvokeVoid(moduleType, module, "OnRunLifecycleChanged", [interruptedExit]);
SetProperty(runContext, "CurrentServerRunId", "run-module-test-2");
SetProperty(runContext, "IsInGameRun", true);
var newRun = Activator.CreateInstance(runInitializedObservedType)!;
SetProperty(newRun, "RunId", "run-module-test-2");
InvokeVoid(moduleType, module, "OnRunInitializedObserved", [newRun]);

Assert(
    store.MarkRunAbandonedCalls == 1,
    "A new run id after interruption should abandon the old run."
);
Assert(
    manager.ActiveSession?.RunId == "run-module-test-2",
    "A new run id should start a fresh active session."
);

capturedLogs.Clear();
var mismatchManifest = new PvpBattleManifest
{
    BattleId = "76543210-full-battle-id",
    RunId = "abcdef12-full-run-id",
    CombatKind = "PVPCombat",
};
var mismatchRecorded = Activator.CreateInstance(pvpBattleRecordedType)!;
SetProperty(mismatchRecorded, "Manifest", mismatchManifest);
InvokeVoid(moduleType, module, "OnPvpBattleRecorded", [mismatchRecorded]);
var mismatchLog = AssertSingleLog(capturedLogs, "run_logging.battle.capture_failed");
Assert(mismatchLog.Level == LogLevel.Error, "A dropped battle record should be Error.");
var mismatchText = mismatchLog.Data?.ToString() ?? string.Empty;
Assert(mismatchText.Contains("run_id=abcdef12"), "Battle failure should correlate the run.");
Assert(mismatchText.Contains("battle_id=76543210"), "Battle failure should correlate the battle.");
Assert(mismatchText.Contains("reason_code=in_run_mismatch"), "Mismatch reason should be typed.");
Assert(
    !mismatchText.Contains("abcdef12-full-run-id")
        && !mismatchText.Contains("76543210-full-battle-id"),
    "Battle failure logs should not expose full correlation ids."
);

var pendingReplayModule =
    Activator.CreateInstance(
        moduleType,
        [
            eventBus,
            runContext,
            manager,
            core,
            new Func<bool>(() => pendingReplayPersistence),
            new Func<RunLogSessionState?>(EnsureActiveSessionFromGame),
            new Func<DateTime>(() => now.UtcDateTime),
            new Func<string, RunLogCompletion>(reason => new RunLogCompletion
            {
                SchemaVersion = 1,
                Status = "completed",
                EndedAtUtc = now,
                Reason = reason,
            }),
            new Func<string, RunLogAbandonment>(reason => new RunLogAbandonment
            {
                SchemaVersion = 1,
                Status = "abandoned",
                EndedAtUtc = now,
                Reason = reason,
            }),
        ]
    ) ?? throw new InvalidOperationException("Failed to construct deferred RunLoggingModule.");

var completedExit = Activator.CreateInstance(runLifecycleChangedType)!;
SetProperty(completedExit, "IsInGameRun", false);
SetProperty(completedExit, "LastRunExitKind", Enum.Parse(runExitKindType, "Completed"));
SetProperty(completedExit, "Reason", "Run ended");
pendingReplayPersistence = true;
InvokeVoid(moduleType, pendingReplayModule, "OnRunLifecycleChanged", [completedExit]);
SetProperty(runContext, "IsInGameRun", false);

Assert(
    GetField(pendingReplayModule, "_deferredRunCompletion") != null,
    "Run end with pending replay persistence should create deferred completion."
);
Assert(
    store.CompleteRunCalls == 0,
    "Deferred completion should wait for replay persistence to drain."
);

now = now.AddSeconds(3);
store.ThrowOnCompleteRun = true;
capturedLogs.Clear();
var failedPersistenceDrain = Activator.CreateInstance(combatReplayPersistenceDrainedType)!;
InvokeVoid(
    moduleType,
    pendingReplayModule,
    "OnCombatReplayPersistenceDrained",
    [failedPersistenceDrain]
);
var drainFailureLog = AssertSingleLog(capturedLogs, "run_logging.replay_drain.handling_failed");
Assert(drainFailureLog.Level == LogLevel.Error, "A drain completion throw should be Error.");
Assert(
    capturedLogs.All(args =>
        args.Data?.ToString()?.Contains("run_logging.run.completion_degraded") != true
    ),
    "A failed completion must not also claim successful degraded completion."
);
store.ThrowOnCompleteRun = false;

pendingReplayPersistence = false;
var persistenceDrained = Activator.CreateInstance(combatReplayPersistenceDrainedType)!;
InvokeVoid(
    moduleType,
    pendingReplayModule,
    "OnCombatReplayPersistenceDrained",
    [persistenceDrained]
);

Assert(
    store.CompleteRunCalls == 1,
    "Draining replay persistence should complete the deferred run."
);

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
        throw new InvalidOperationException(
            $"Field not found: {instance.GetType().FullName}.{name}"
        );

    return field.GetValue(instance);
}

static object? GetProperty(Type type, object instance, string name)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    return property.GetValue(instance);
}

static void SetProperty(object instance, string name, object? value)
{
    var property = instance
        .GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
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

static LogEventArgs AssertSingleLog(IReadOnlyList<LogEventArgs> logs, string eventId)
{
    var matches = logs.Where(args => args.Data?.ToString()?.Contains("event=" + eventId) == true)
        .ToArray();
    if (matches.Length != 1)
    {
        throw new InvalidOperationException(
            $"Expected one {eventId} event, found {matches.Length}: "
                + string.Join(" | ", logs.Select(args => args.Data?.ToString()))
        );
    }
    return matches[0];
}

static void InstallBppLog(ManualLogSource source)
{
    var bppLogType = RequireType("BazaarPlusPlus.Infrastructure.BppLog");
    var install = bppLogType.GetMethod(
        "Install",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
    );
    if (install == null)
        throw new InvalidOperationException("BppLog.Install was not found.");
    install.Invoke(null, [source]);
}

file sealed class FakeRunLogStore : IRunLogStore
{
    public int CompleteRunCalls { get; private set; }

    public int MarkRunAbandonedCalls { get; private set; }

    public bool ThrowOnCompleteRun { get; set; }

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
        if (ThrowOnCompleteRun)
            throw new InvalidOperationException("complete run failed");
        CompleteRunCalls++;
        ActiveState = null;
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        MarkRunAbandonedCalls++;
        ActiveState = null;
    }
}
