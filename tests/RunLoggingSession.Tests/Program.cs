#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;

var managerType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogSessionManager");
var ctor = managerType.GetConstructor([typeof(IRunLogStore), typeof(Func<DateTimeOffset>)]);
Assert(
    ctor != null,
    "RunLogSessionManager should expose a constructor taking IRunLogStore and a clock."
);

var fakeStore = new FakeRunLogStore();
var now = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
var manager = ctor!.Invoke([fakeStore, new Func<DateTimeOffset>(() => now)]);

var request = new RunLogCreateRequest
{
    SchemaVersion = 1,
    RunId = "run_20260315t121530z_vanessa_ranked_002a_deadbeef",
    StartedAtUtc = now,
    Hero = "Vanessa",
    GameMode = "Ranked",
    Day = 1,
    Hour = 1,
    Seed = 42,
};

Invoke<RunLogSessionState>(managerType, manager, "EnsureActiveSession", [request]);
Invoke<RunLogSessionState>(managerType, manager, "EnsureActiveSession", [request]);
Assert(fakeStore.CreateRunCalls == 1, "EnsureActiveSession should create a run only once.");

var firstSelection = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "selection_seen",
            Day = 1,
            Hour = 1,
            State = "Encounter",
            SelectionFingerprint = "selection-fp-1",
        },
    ]
);
Assert(firstSelection != null, "The first selection_seen event should be recorded.");
Assert(firstSelection!.Seq == 1, "The first recorded event should get seq=1.");
Assert(fakeStore.AppendedEvents.Count == 1, "The first selection event should be persisted.");
Assert(
    fakeStore.ResumeState?.PendingSelection?.SelectionSeq == 1,
    "selection_seen should persist the pending selection payload."
);

var duplicateSelection = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "selection_seen",
            Day = 1,
            Hour = 1,
            State = "Encounter",
            SelectionFingerprint = "selection-fp-1",
        },
    ]
);
Assert(duplicateSelection == null, "Duplicate selection_seen fingerprints should be suppressed.");
Assert(fakeStore.AppendedEvents.Count == 1, "Duplicate selection_seen should not be persisted.");

var firstStateEvent = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "state_seen",
            Day = 1,
            Hour = 1,
            State = "Encounter",
            StateFingerprint = "state-fp-1",
        },
    ]
);
Assert(firstStateEvent != null, "The first state_seen event should be recorded.");

var duplicateStateEvent = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "state_seen",
            Day = 1,
            Hour = 1,
            State = "Encounter",
            StateFingerprint = "state-fp-1",
        },
    ]
);
Assert(duplicateStateEvent == null, "Duplicate state_seen fingerprints should be suppressed.");
Assert(
    fakeStore.AppendedEvents.Count(e => e.Kind == "state_seen") == 1,
    "Duplicate state_seen should not be persisted."
);

var progressEvent = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "run_progress",
            Day = 1,
            Hour = 2,
        },
    ]
);
Assert(progressEvent != null, "A non-duplicate event should be persisted.");
Assert(progressEvent!.Seq == 3, "Sequence numbers should remain monotonic after suppression.");
Assert(fakeStore.AppendedEvents.Count == 3, "Three unique events should be persisted.");

var choiceEvent = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "choice_made",
            SelectionSeq = 1,
            SelectedInstanceId = "instance-a",
            SelectedTemplateId = "template-a",
            SelectedName = "Frost Street",
        },
    ]
);
Assert(choiceEvent != null, "choice_made should be accepted for the pending selection.");
Assert(choiceEvent!.Seq == 4, "choice_made should advance the sequence.");
Assert(
    fakeStore.ResumeState?.PendingSelectionSeq == null,
    "choice_made should clear pending_selection_seq."
);
Assert(
    fakeStore.ResumeState?.PendingSelection == null,
    "choice_made should clear the pending selection payload."
);

var secondSelection = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "choice_options_seen",
            Day = 1,
            Hour = 2,
            State = "Choice",
            SelectionFingerprint = "selection-fp-2",
        },
    ]
);
Assert(secondSelection != null, "A second selection should be recorded.");

var abandonedSelection = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "selection_abandoned",
            Day = 1,
            Hour = 2,
            State = "Choice",
            SelectionSeq = secondSelection!.Seq,
            AbandonedReason = "superseded_by_new_selection",
        },
    ]
);
Assert(abandonedSelection != null, "selection_abandoned should be recorded.");
Assert(
    fakeStore.ResumeState?.PendingSelectionSeq == null
        && fakeStore.ResumeState?.PendingSelection == null,
    "selection_abandoned should clear the pending selection state."
);

var staleChoiceEvent = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "choice_made",
            SelectionSeq = secondSelection!.Seq,
            SelectedInstanceId = "instance-a",
        },
    ]
);
Assert(
    staleChoiceEvent == null,
    "A duplicate/stale choice_made should be suppressed once the pending selection is cleared."
);

Assert(fakeStore.ResumeState != null, "The fake store should retain the active session.");
fakeStore.ResumeState!.Day = 6;
fakeStore.ResumeState.Hour = 4;
fakeStore.ResumeState.MaxHealth = 90;
fakeStore.ResumeState.Prestige = 7;
fakeStore.ResumeState.Level = 5;
fakeStore.ResumeState.Income = 3;
fakeStore.ResumeState.Gold = 12;

InvokeVoid(
    managerType,
    manager,
    "CompleteRun",
    [new RunLogCompletion { Status = "completed", EndedAtUtc = now.AddMinutes(10) }]
);
Assert(fakeStore.CompleteRunCalls == 1, "CompleteRun should call the store exactly once.");
Assert(
    fakeStore.LastCompletion != null,
    "CompleteRun should pass the resolved completion payload to the store."
);
Assert(
    fakeStore.LastCompletion!.FinalDay == 6 && fakeStore.LastCompletion.FinalHour == 4,
    "CompleteRun should fall back to the session day/hour when the completion payload omits them."
);
Assert(
    fakeStore.LastCompletion.MaxHealth == 90
        && fakeStore.LastCompletion.Prestige == 7
        && fakeStore.LastCompletion.Level == 5
        && fakeStore.LastCompletion.Income == 3
        && fakeStore.LastCompletion.Gold == 12,
    "CompleteRun should fall back to the last checkpoint stats when the completion payload omits them."
);
Assert(
    !GetProperty<bool>(managerType, manager, "HasActiveSession"),
    "CompleteRun should clear the active session."
);

fakeStore.ResumeState = new RunLogSessionState
{
    RunId = request.RunId,
    SchemaVersion = 1,
    StartedAtUtc = now,
    LastSeenAtUtc = now.AddMinutes(5),
    LastSeq = 41,
    Day = 4,
    Hour = 2,
    State = "Choice",
    LastSelectionFingerprint = "selection-fp-prev",
    PendingSelectionSeq = 40,
    PendingSelection = new RunLogPendingSelectionState
    {
        Day = 4,
        Hour = 2,
        State = "Choice",
        EncounterId = "enc-40",
        ParentEncounterId = "enc-40",
        SelectionSeq = 40,
        Options =
        [
            new RunLogOptionSnapshot
            {
                InstanceId = "instance-prev",
                TemplateId = "template-prev",
                Name = "Recovered Choice",
            },
        ],
    },
};

var resumedManager = ctor.Invoke([fakeStore, new Func<DateTimeOffset>(() => now.AddMinutes(20))]);
var resumedState = Invoke<RunLogSessionState>(
    managerType,
    resumedManager,
    "EnsureActiveSession",
    [request]
);
Assert(
    resumedState.RunId == request.RunId,
    "EnsureActiveSession should prefer the resumable run when the server run id matches."
);
Assert(fakeStore.CreateRunCalls == 1, "Resuming should not create a second run.");

var resumedEvent = Invoke<RunLogEvent?>(
    managerType,
    resumedManager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "run_progress",
            Day = 4,
            Hour = 3,
        },
    ]
);
Assert(resumedEvent != null, "Resumed manager should accept events.");
Assert(resumedEvent!.RunId == request.RunId, "Resumed events should target the restored run id.");
Assert(resumedEvent.Seq == 42, "Resumed sequencing should continue from the persisted last_seq.");

fakeStore.ResumeState = new RunLogSessionState
{
    RunId = "server-run-old",
    SchemaVersion = 1,
    StartedAtUtc = now,
    LastSeenAtUtc = now.AddMinutes(6),
    LastSeq = 7,
    Day = 3,
    Hour = 2,
};

var replacementRequest = new RunLogCreateRequest
{
    SchemaVersion = 1,
    RunId = "server-run-new",
    StartedAtUtc = now.AddMinutes(30),
    Hero = "Vanessa",
    GameMode = "Ranked",
    Day = 1,
    Hour = 1,
};
var replacementManager = ctor.Invoke([
    fakeStore,
    new Func<DateTimeOffset>(() => now.AddMinutes(30)),
]);
var replacementState = Invoke<RunLogSessionState>(
    managerType,
    replacementManager,
    "EnsureActiveSession",
    [replacementRequest]
);
Assert(
    replacementState.RunId == "server-run-new",
    "EnsureActiveSession should replace a restored session when the server run id changes."
);
Assert(fakeStore.MarkRunAbandonedCalls == 1, "A mismatched restored session should be abandoned.");
Assert(fakeStore.CreateRunCalls == 2, "A mismatched restored session should create a fresh run.");

var runLoggingControllerPath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Game/RunLogging/RunLoggingController.cs"
    )
);
var runLoggingControllerSource = File.ReadAllText(runLoggingControllerPath);
Assert(
    runLoggingControllerSource.Contains(
        "public RunLogEvent? AcceptStateSnapshot",
        StringComparison.Ordinal
    ),
    "AcceptStateSnapshot should return a nullable event so duplicate state snapshots can be ignored without throwing."
);
Assert(
    !runLoggingControllerSource.Contains(
        "State event was unexpectedly suppressed.",
        StringComparison.Ordinal
    ),
    "AcceptStateSnapshot should not throw when duplicate state snapshots are suppressed."
);

var runLoggingModulePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Game/RunLogging/RunLoggingModule.cs")
);
var runLoggingModuleSource = File.ReadAllText(runLoggingModulePath);
var runLoggingStopBody = ExtractMethodBody(runLoggingModuleSource, "public void Stop()");
Assert(
    runLoggingStopBody.Contains(
        "TryCompleteDeferredRunExit(forceCompletion: true);",
        StringComparison.Ordinal
    )
        && runLoggingStopBody.IndexOf(
            "TryCompleteDeferredRunExit(forceCompletion: true);",
            StringComparison.Ordinal
        )
            < runLoggingStopBody.IndexOf(
                "_deferredRunCompletion = null;",
                StringComparison.Ordinal
            ),
    "RunLoggingModule.Stop should force a deferred run completion before clearing deferred exit state during teardown."
);
var runLoggingSyncBody = ExtractMethodBody(
    runLoggingModuleSource,
    "private void OnRunLoggingSyncRequested(RunLoggingSyncRequested request)"
);
Assert(
    runLoggingSyncBody.Contains("_hasPendingReplayPersistence()", StringComparison.Ordinal),
    "RunLoggingModule should inspect injected replay persistence state before completing a run on exit."
);
Assert(
    runLoggingModuleSource.Contains("TryCompleteDeferredRunExit", StringComparison.Ordinal)
        && runLoggingModuleSource.Contains("DateTime.UtcNow", StringComparison.Ordinal)
        && runLoggingModuleSource.Contains("TimeSpan.FromSeconds", StringComparison.Ordinal),
    "RunLoggingModule should defer run completion until replay persistence drains or a short grace window expires."
);
Assert(
    !runLoggingModuleSource.Contains("CombatReplayRuntime.Instance", StringComparison.Ordinal),
    "RunLoggingModule should not reach into CombatReplayRuntime.Instance directly."
);
var pvpBattleRecordedBody = ExtractMethodBody(
    runLoggingModuleSource,
    "private void OnPvpBattleRecorded(PvpBattleRecorded recorded)"
);
Assert(
    !pvpBattleRecordedBody.Contains(
        "|| !BppRuntimeHost.RunContext.IsInGameRun",
        StringComparison.Ordinal
    )
        && pvpBattleRecordedBody.Contains("_sessionManager.HasActiveSession", StringComparison.Ordinal),
    "RunLoggingModule should still accept post-persist PVP replay events while a deferred active session remains open."
);

var historyBridgePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Game/HistoryPanel/HistoryCollectionsEntryBridge.cs"
    )
);
var historyBridgeSource = File.ReadAllText(historyBridgePath);
Assert(
    historyBridgeSource.Contains("_cachedAnchorButton", StringComparison.Ordinal),
    "HistoryCollectionsEntryBridge should cache the detected collections anchor between scans."
);

var historyPanelPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Game/HistoryPanel/HistoryPanel.cs")
);
var historyPanelSource = File.ReadAllText(historyPanelPath);
var historyPanelCanvasPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Game/HistoryPanel/HistoryPanel.Canvas.cs")
);
var historyPanelCanvasSource = File.ReadAllText(historyPanelCanvasPath);
var historyPanelAwakeBody = ExtractMethodBody(historyPanelSource, "private void Awake()");
Assert(
    !historyPanelAwakeBody.Contains("RefreshData();", StringComparison.Ordinal),
    "HistoryPanel.Awake should not load sqlite data before the panel is opened."
);
var historyPanelVisibilityBody = ExtractMethodBody(
    historyPanelSource,
    "private void SetHistoryVisible(bool visible)"
);
Assert(
    historyPanelVisibilityBody.Contains("RefreshData();", StringComparison.Ordinal),
    "HistoryPanel should still load data when the panel becomes visible."
);
Assert(
    historyPanelSource.Contains(
        "private bool CanReplaySelectedBattle(out string reason)",
        StringComparison.Ordinal
    ),
    "HistoryPanel should centralize selected-battle replay availability checks."
);
var tryReplaySelectedBattleBody = ExtractMethodBody(
    historyPanelSource,
    "private void TryReplaySelectedBattle()"
);
Assert(
    tryReplaySelectedBattleBody.Contains("CanReplaySelectedBattle(", StringComparison.Ordinal),
    "HistoryPanel should reuse the shared replay availability helper before starting a replay."
);
Assert(
    historyPanelCanvasSource.Contains("CanReplaySelectedBattle(", StringComparison.Ordinal)
        && historyPanelCanvasSource.Contains("Replay unavailable:", StringComparison.Ordinal),
    "HistoryPanel UI should disable replay and explain why when the selected battle cannot be replayed."
);

Console.WriteLine("RunLogging session checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static T Invoke<T>(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    return (T)method.Invoke(instance, args)!;
}

static void InvokeVoid(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    method.Invoke(instance, args);
}

static T GetProperty<T>(Type type, object instance, string name)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    return (T)property.GetValue(instance)!;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static string ExtractMethodBody(string source, string signature)
{
    var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
    if (signatureIndex < 0)
        throw new InvalidOperationException($"Method signature not found: {signature}");

    var bodyStart = source.IndexOf('{', signatureIndex);
    if (bodyStart < 0)
        throw new InvalidOperationException($"Method body start not found: {signature}");

    var depth = 0;
    for (var index = bodyStart; index < source.Length; index++)
    {
        if (source[index] == '{')
            depth++;
        else if (source[index] == '}')
            depth--;

        if (depth == 0)
            return source.Substring(bodyStart + 1, index - bodyStart - 1);
    }

    throw new InvalidOperationException($"Method body end not found: {signature}");
}

file sealed class FakeRunLogStore : IRunLogStore
{
    public int CreateRunCalls { get; private set; }

    public int CompleteRunCalls { get; private set; }

    public int MarkRunAbandonedCalls { get; private set; }

    public List<RunLogEvent> AppendedEvents { get; } = [];

    public RunLogCompletion? LastCompletion { get; private set; }

    public RunLogSessionState? ResumeState { get; set; }

    public RunLogSessionState? TryResumeActiveRun()
    {
        return ResumeState;
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        CreateRunCalls++;
        ResumeState = new RunLogSessionState
        {
            RunId = request.RunId,
            SchemaVersion = request.SchemaVersion,
            StartedAtUtc = request.StartedAtUtc,
            LastSeenAtUtc = request.StartedAtUtc,
            LastSeq = 0,
            Day = request.Day,
            Hour = request.Hour,
            MaxHealth = 90,
            Prestige = 7,
            Level = 5,
            Income = 3,
            Gold = 12,
            Completed = false,
        };
        return ResumeState;
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        AppendedEvents.Add(entry);
        if (ResumeState != null)
        {
            ResumeState.LastSeq = entry.Seq;
            ResumeState.LastSeenAtUtc = entry.Ts;
            ResumeState.Day = entry.Day;
            ResumeState.Hour = entry.Hour;
            ResumeState.State = entry.State;
            if (!string.IsNullOrWhiteSpace(entry.SelectionFingerprint))
                ResumeState.LastSelectionFingerprint = entry.SelectionFingerprint;
        }
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        if (ResumeState != null)
        {
            ResumeState.LastSeq = checkpoint.LastSeq;
            ResumeState.LastSeenAtUtc = checkpoint.LastSeenAtUtc;
            ResumeState.Day = checkpoint.Day;
            ResumeState.Hour = checkpoint.Hour;
            ResumeState.State = checkpoint.State;
            ResumeState.CurrentEncounterId = checkpoint.CurrentEncounterId;
            ResumeState.LastStateFingerprint = checkpoint.LastStateFingerprint;
            ResumeState.LastSelectionFingerprint = checkpoint.LastSelectionFingerprint;
            ResumeState.PendingSelectionSeq = checkpoint.PendingSelectionSeq;
            ResumeState.PendingSelection = checkpoint.PendingSelection;
            ResumeState.Completed = checkpoint.Completed;
        }
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        CompleteRunCalls++;
        LastCompletion = completion;
        ResumeState = null;
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        MarkRunAbandonedCalls++;
        ResumeState = null;
    }
}
