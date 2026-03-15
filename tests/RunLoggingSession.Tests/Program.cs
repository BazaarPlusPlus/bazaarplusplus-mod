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
Assert(progressEvent!.Seq == 2, "Sequence numbers should remain monotonic after suppression.");
Assert(fakeStore.AppendedEvents.Count == 2, "Two unique events should be persisted.");

InvokeVoid(
    managerType,
    manager,
    "CompleteRun",
    [
        new RunLogCompletion
        {
            Status = "completed",
            EndedAtUtc = now.AddMinutes(10),
        },
    ]
);
Assert(fakeStore.CompleteRunCalls == 1, "CompleteRun should call the store exactly once.");
Assert(
    !GetProperty<bool>(managerType, manager, "HasActiveSession"),
    "CompleteRun should clear the active session."
);

fakeStore.ResumeState = new RunLogSessionState
{
    RunId = "run_existing",
    SchemaVersion = 1,
    StartedAtUtc = now,
    LastSeenAtUtc = now.AddMinutes(5),
    LastSeq = 41,
    Day = 4,
    Hour = 2,
    State = "Choice",
    LastSelectionFingerprint = "selection-fp-prev",
    PendingSelectionSeq = 40,
};

var resumedManager = ctor.Invoke([fakeStore, new Func<DateTimeOffset>(() => now.AddMinutes(20))]);
var resumedState = Invoke<RunLogSessionState>(managerType, resumedManager, "EnsureActiveSession", [request]);
Assert(resumedState.RunId == "run_existing", "EnsureActiveSession should prefer the resumable run.");
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
Assert(resumedEvent!.RunId == "run_existing", "Resumed events should target the restored run id.");
Assert(resumedEvent.Seq == 42, "Resumed sequencing should continue from the persisted last_seq.");

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

file sealed class FakeRunLogStore : IRunLogStore
{
    public int CreateRunCalls { get; private set; }

    public int CompleteRunCalls { get; private set; }

    public List<RunLogEvent> AppendedEvents { get; } = [];

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
            ResumeState.Completed = checkpoint.Completed;
        }
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        CompleteRunCalls++;
        ResumeState = null;
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        ResumeState = null;
    }
}
