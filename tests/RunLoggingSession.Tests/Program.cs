#nullable enable
using System.Reflection;
using System.Threading.Tasks;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog;

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

var combatEvent = Invoke<RunLogEvent?>(
    managerType,
    manager,
    "AppendEvent",
    [
        new RunLogEvent
        {
            Kind = "pvp_combat_recorded",
            Day = 1,
            Hour = 2,
            EncounterId = "encounter-pvp-1",
            BattleId = "battle-123",
        },
    ]
);
Assert(combatEvent != null, "A pvp_combat_recorded event should be persisted.");
Assert(combatEvent!.Seq == 1, "The first recorded event should get seq=1.");
Assert(fakeStore.AppendedEvents.Count == 1, "Only the combat event should be persisted.");

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
};

var restoredManager = ctor.Invoke([fakeStore, new Func<DateTimeOffset>(() => now.AddMinutes(12))]);
Invoke<RunLogSessionState?>(managerType, restoredManager, "RestoreActiveSession", []);
Assert(
    GetProperty<bool>(managerType, restoredManager, "HasActiveSession"),
    "RestoreActiveSession should restore the active run."
);

InvokeVoid(
    managerType,
    restoredManager,
    "MarkRunAbandoned",
    [new RunLogAbandonment { Status = "abandoned", EndedAtUtc = now.AddMinutes(15) }]
);
Assert(
    fakeStore.MarkRunAbandonedCalls == 1,
    "MarkRunAbandoned should call the store exactly once."
);
Assert(
    !GetProperty<bool>(managerType, restoredManager, "HasActiveSession"),
    "MarkRunAbandoned should clear the active session."
);

var mismatchStore = new FakeRunLogStore();
mismatchStore.ResumeState = new RunLogSessionState
{
    RunId = "server-run-old",
    SchemaVersion = 1,
    StartedAtUtc = now,
    LastSeenAtUtc = now.AddMinutes(2),
    LastSeq = 5,
    Day = 1,
    Hour = 1,
};
var mismatchManager = ctor.Invoke([
    mismatchStore,
    new Func<DateTimeOffset>(() => now.AddMinutes(20)),
]);
Invoke<RunLogSessionState>(
    managerType,
    mismatchManager,
    "EnsureActiveSession",
    [
        new RunLogCreateRequest
        {
            SchemaVersion = 1,
            RunId = "server-run-new",
            StartedAtUtc = now.AddMinutes(20),
            Hero = "Vanessa",
            GameMode = "Ranked",
            Day = 1,
            Hour = 1,
        },
    ]
);
Assert(
    mismatchStore.MarkRunAbandonedCalls == 1,
    "A mismatched restored session should be abandoned."
);
Assert(
    mismatchStore.LastAbandonment?.Reason == "session_mismatch",
    "Mismatched sessions should be abandoned with the session_mismatch reason."
);

Console.WriteLine("RunLogging session checks passed.");

static Type RequireType(string fullName)
{
    return Assembly.Load("BazaarPlusPlus").GetType(fullName, throwOnError: true)!;
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

    public int MarkRunAbandonedCalls { get; private set; }

    public List<RunLogEvent> AppendedEvents { get; } = [];

    public RunLogSessionState? ResumeState { get; set; }

    public RunLogCompletion? LastCompletion { get; private set; }

    public RunLogAbandonment? LastAbandonment { get; private set; }

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
        }
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        if (ResumeState == null)
            return;

        ResumeState.LastSeq = checkpoint.LastSeq;
        ResumeState.LastSeenAtUtc = checkpoint.LastSeenAtUtc;
        ResumeState.Day = checkpoint.Day;
        ResumeState.Hour = checkpoint.Hour;
        ResumeState.MaxHealth = checkpoint.MaxHealth;
        ResumeState.Prestige = checkpoint.Prestige;
        ResumeState.Level = checkpoint.Level;
        ResumeState.Income = checkpoint.Income;
        ResumeState.Gold = checkpoint.Gold;
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
        LastAbandonment = abandonment;
        ResumeState = null;
    }
}
