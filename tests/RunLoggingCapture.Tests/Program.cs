#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;

var captureServiceType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogCaptureService");
var snapshotBuilderType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogSnapshotBuilder");
var progressInputType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogRunProgressInput");
var stateInputType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogStateSnapshotInput");
var selectionInputType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogSelectionSnapshotInput");
var optionInputType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogSelectionOptionInput");

var runInitializedPatchPath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Patches/RunLogging/RunInitializedPatch.cs"
    )
);
Assert(
    File.Exists(runInitializedPatchPath),
    $"RunInitialized patch source not found at {runInitializedPatchPath}"
);
var runInitializedPatchSource = File.ReadAllText(runInitializedPatchPath);
Assert(
    runInitializedPatchSource.Contains("NetMessageRunInitialized", StringComparison.Ordinal),
    "RunInitialized patch should intercept the server run initialization message."
);
Assert(
    runInitializedPatchSource.Contains("CurrentServerRunId", StringComparison.Ordinal),
    "RunInitialized patch should store the authoritative server run id."
);

var service =
    Activator.CreateInstance(captureServiceType)
    ?? throw new InvalidOperationException("RunLogCaptureService should be constructible.");

var progressInput =
    Activator.CreateInstance(progressInputType)
    ?? throw new InvalidOperationException("RunLogRunProgressInput should be constructible.");
SetProperty(progressInputType, progressInput, "Day", 2);
SetProperty(progressInputType, progressInput, "Hour", 1);
SetProperty(progressInputType, progressInput, "Victories", 3);
SetProperty(progressInputType, progressInput, "Losses", 1);
SetProperty(progressInputType, progressInput, "CurrentHourXp", 5);

var progressEvent = Invoke<RunLogEvent>(
    captureServiceType,
    service,
    "BuildRunProgressEvent",
    [progressInput]
);
Assert(progressEvent.Kind == "run_progress", "Run progress should map to a run_progress event.");
Assert(progressEvent.Day == 2, "Run progress should preserve the day.");
Assert(progressEvent.Hour == 1, "Run progress should preserve the hour.");
Assert(progressEvent.Victories == 3, "Run progress should preserve victories.");
Assert(progressEvent.Losses == 1, "Run progress should preserve losses.");
Assert(progressEvent.CurrentHourXp == 5, "Run progress should preserve current_hour_xp.");

var stateInput =
    Activator.CreateInstance(stateInputType)
    ?? throw new InvalidOperationException("RunLogStateSnapshotInput should be constructible.");
SetProperty(stateInputType, stateInput, "Day", 2);
SetProperty(stateInputType, stateInput, "Hour", 2);
SetProperty(stateInputType, stateInput, "State", "Encounter");
SetProperty(stateInputType, stateInput, "EncounterId", "encounter-123");
SetProperty(stateInputType, stateInput, "RerollCost", 4);
SetProperty(stateInputType, stateInput, "RerollsRemaining", 1);

var stateEvent = Invoke<RunLogEvent>(
    captureServiceType,
    service,
    "BuildStateSeenEvent",
    [stateInput]
);
Assert(stateEvent.Kind == "state_seen", "State snapshots should map to a state_seen event.");
Assert(stateEvent.State == "Encounter", "State snapshots should preserve state.");
Assert(stateEvent.EncounterId == "encounter-123", "State snapshots should preserve encounter id.");
Assert(stateEvent.RerollCost == 4, "State snapshots should preserve reroll cost.");
Assert(stateEvent.RerollsRemaining == 1, "State snapshots should preserve rerolls remaining.");

var selectionInput =
    Activator.CreateInstance(selectionInputType)
    ?? throw new InvalidOperationException("RunLogSelectionSnapshotInput should be constructible.");
SetProperty(selectionInputType, selectionInput, "Day", 2);
SetProperty(selectionInputType, selectionInput, "Hour", 2);
SetProperty(selectionInputType, selectionInput, "State", "Encounter");
SetProperty(selectionInputType, selectionInput, "EncounterId", "encounter-123");
SetProperty(
    selectionInputType,
    selectionInput,
    "Options",
    CreateOptionList(
        optionInputType,
        CreateOption(
            optionInputType,
            0,
            "instance-a",
            "template-a",
            "Frost Street",
            "Bronze",
            "None"
        ),
        CreateOption(
            optionInputType,
            1,
            "instance-b",
            "template-b",
            "Amber Cove",
            "Silver",
            "Shiny"
        )
    )
);

var firstFingerprint = Invoke<string>(
    snapshotBuilderType,
    null,
    "ComputeSelectionFingerprint",
    [selectionInput]
);
var secondFingerprint = Invoke<string>(
    snapshotBuilderType,
    null,
    "ComputeSelectionFingerprint",
    [selectionInput]
);
Assert(!string.IsNullOrWhiteSpace(firstFingerprint), "Selection fingerprints should not be blank.");
Assert(firstFingerprint == secondFingerprint, "Selection fingerprints should be deterministic.");

var selectionEvent = Invoke<RunLogEvent>(
    captureServiceType,
    service,
    "BuildSelectionSeenEvent",
    [selectionInput]
);
Assert(
    selectionEvent.Kind == "selection_seen",
    "Selection snapshots should map to selection_seen."
);
Assert(
    selectionEvent.SelectionFingerprint == firstFingerprint,
    "Selection snapshots should use the deterministic fingerprint."
);
Assert(selectionEvent.Options.Count == 2, "Selection snapshots should project every option.");
Assert(
    selectionEvent.Options[0].InstanceId == "instance-a",
    "Option projection should preserve instance_id."
);
Assert(
    selectionEvent.Options[0].TemplateId == "template-a",
    "Option projection should preserve template_id."
);
Assert(selectionEvent.Options[0].Name == "Frost Street", "Option projection should preserve name.");
Assert(selectionEvent.Options[0].Tier == "Bronze", "Option projection should preserve tier.");
Assert(selectionEvent.Options[0].Enchant == "None", "Option projection should preserve enchant.");

var seamStore = new ControllerSeamStore();
var seamSessionManager = new RunLogSessionManager(
    seamStore,
    () => new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero)
);
var seamCaptureService = new RunLogCaptureService();
var coreType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingControllerCore");
var coreCtor = coreType.GetConstructor([
    typeof(RunLogSessionManager),
    typeof(RunLogCaptureService),
]);
Assert(
    coreCtor != null,
    "RunLoggingControllerCore should accept session manager and capture service."
);
var core = coreCtor!.Invoke([seamSessionManager, seamCaptureService]);

Invoke<object>(
    coreType,
    core,
    "EnsureRunStarted",
    [
        new RunLogCreateRequest
        {
            SchemaVersion = 1,
            RunId = "run_20260315t121530z_vanessa_ranked_002a_deadbeef",
            StartedAtUtc = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero),
            Hero = "Vanessa",
            GameMode = "Ranked",
            Day = 1,
            Hour = 1,
        },
    ]
);
Invoke<object>(
    coreType,
    core,
    "AcceptRunProgress",
    [
        new RunLogRunProgressInput
        {
            Day = 1,
            Hour = 2,
            Victories = 1,
            Losses = 0,
        },
    ]
);
Invoke<object>(
    coreType,
    core,
    "AcceptStateSnapshot",
    [
        new RunLogStateSnapshotInput
        {
            Day = 1,
            Hour = 2,
            State = "Encounter",
            EncounterId = "encounter-123",
        },
    ]
);
Invoke<object>(
    coreType,
    core,
    "AcceptSelectionSnapshot",
    [
        new RunLogSelectionSnapshotInput
        {
            Day = 1,
            Hour = 2,
            State = "Encounter",
            EncounterId = "encounter-123",
            Options =
            [
                new RunLogSelectionOptionInput
                {
                    Index = 0,
                    InstanceId = "instance-a",
                    TemplateId = "template-a",
                    Name = "Frost Street",
                    Tier = "Bronze",
                    Enchant = "None",
                },
            ],
        },
    ]
);
Invoke<object>(
    coreType,
    core,
    "CompleteRun",
    [
        new RunLogCompletion
        {
            Status = "completed",
            EndedAtUtc = new DateTimeOffset(2026, 3, 15, 12, 30, 0, TimeSpan.Zero),
        },
    ]
);

Assert(
    seamStore
        .AppendedEvents.Select(e => e.Kind)
        .SequenceEqual(["run_started", "run_progress", "state_seen", "selection_seen"]),
    "Controller seam should forward events in run-started to selection-seen order."
);
Assert(seamStore.CompleteRunCalls == 1, "Controller seam should forward completion once.");

var resumedStore = new ControllerSeamStore
{
    ResumeState = new RunLogSessionState
    {
        RunId = "run_20260315t131530z_vanessa_ranked_002b_feedface",
        SchemaVersion = 1,
        StartedAtUtc = new DateTimeOffset(2026, 3, 15, 13, 15, 30, TimeSpan.Zero),
        LastSeenAtUtc = new DateTimeOffset(2026, 3, 15, 13, 20, 0, TimeSpan.Zero),
        LastSeq = 3,
        Day = 2,
        Hour = 1,
        State = "Encounter",
    },
};
var resumedSessionManager = new RunLogSessionManager(
    resumedStore,
    () => new DateTimeOffset(2026, 3, 15, 13, 25, 0, TimeSpan.Zero)
);
resumedSessionManager.RestoreActiveSession();
var resumedCore = coreCtor.Invoke([resumedSessionManager, seamCaptureService]);
Invoke<object>(
    coreType,
    resumedCore,
    "EnsureRunStarted",
    [
        new RunLogCreateRequest
        {
            SchemaVersion = 1,
            RunId = "run_20260315t131530z_vanessa_ranked_002b_feedface",
            StartedAtUtc = new DateTimeOffset(2026, 3, 15, 13, 15, 30, TimeSpan.Zero),
            Hero = "Vanessa",
            GameMode = "Ranked",
            Day = 2,
            Hour = 1,
        },
    ]
);

Assert(
    resumedStore.AppendedEvents.Select(e => e.Kind).SequenceEqual(["run_resumed"]),
    "Restored sessions should append a single run_resumed event instead of duplicating run_started."
);
Assert(
    resumedStore.AppendedEvents[0].Seq == 4,
    "Restored sessions should continue sequence numbers when appending run_resumed."
);

var mismatchStore = new ControllerSeamStore
{
    ResumeState = new RunLogSessionState
    {
        RunId = "server-run-old",
        SchemaVersion = 1,
        StartedAtUtc = new DateTimeOffset(2026, 3, 15, 14, 0, 0, TimeSpan.Zero),
        LastSeenAtUtc = new DateTimeOffset(2026, 3, 15, 14, 5, 0, TimeSpan.Zero),
        LastSeq = 5,
        Day = 3,
        Hour = 1,
    },
};
var mismatchSessionManager = new RunLogSessionManager(
    mismatchStore,
    () => new DateTimeOffset(2026, 3, 15, 14, 10, 0, TimeSpan.Zero)
);
mismatchSessionManager.RestoreActiveSession();
var mismatchCore = coreCtor.Invoke([mismatchSessionManager, seamCaptureService]);
Invoke<object>(
    coreType,
    mismatchCore,
    "EnsureRunStarted",
    [
        new RunLogCreateRequest
        {
            SchemaVersion = 1,
            RunId = "server-run-new",
            StartedAtUtc = new DateTimeOffset(2026, 3, 15, 14, 10, 0, TimeSpan.Zero),
            Hero = "Vanessa",
            GameMode = "Ranked",
            Day = 1,
            Hour = 1,
        },
    ]
);
Assert(
    mismatchStore.MarkRunAbandonedCalls == 1,
    "A mismatched restored session should be abandoned before the new run starts."
);
Assert(
    mismatchStore.AppendedEvents.Select(e => e.Kind).SequenceEqual(["run_started"]),
    "Switching to a new server run id should still emit run_started for the fresh run."
);

Console.WriteLine("RunLogging capture checks passed.");

static object CreateOption(
    Type optionInputType,
    int index,
    string instanceId,
    string templateId,
    string name,
    string tier,
    string enchant
)
{
    var option =
        Activator.CreateInstance(optionInputType)
        ?? throw new InvalidOperationException(
            "RunLogSelectionOptionInput should be constructible."
        );
    SetProperty(optionInputType, option, "Index", index);
    SetProperty(optionInputType, option, "InstanceId", instanceId);
    SetProperty(optionInputType, option, "TemplateId", templateId);
    SetProperty(optionInputType, option, "Name", name);
    SetProperty(optionInputType, option, "Tier", tier);
    SetProperty(optionInputType, option, "Enchant", enchant);
    return option;
}

static object CreateOptionList(Type optionInputType, params object[] options)
{
    var listType = typeof(List<>).MakeGenericType(optionInputType);
    var list =
        Activator.CreateInstance(listType)
        ?? throw new InvalidOperationException($"Failed to construct {listType.FullName}.");
    var addMethod =
        listType.GetMethod("Add", [optionInputType])
        ?? throw new InvalidOperationException($"Add method not found on {listType.FullName}.");
    foreach (var option in options)
        addMethod.Invoke(list, [option]);

    return list;
}

static Type RequireType(string fullName)
{
    var assembly = typeof(RunLogCaptureService).Assembly;
    return assembly.GetType(fullName, throwOnError: false)
        ?? assembly
            .GetTypes()
            .FirstOrDefault(type =>
                type.FullName == fullName || type.Name == fullName.Split('.').Last()
            )
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void SetProperty(Type type, object instance, string name, object? value)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    property.SetValue(instance, value);
}

static T Invoke<T>(Type type, object? instance, string name, object?[] args)
{
    var flags = BindingFlags.Public | BindingFlags.InvokeMethod;
    flags |= instance == null ? BindingFlags.Static : BindingFlags.Instance;
    var method = type.GetMethod(name, flags);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    return (T)method.Invoke(instance, args)!;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

file sealed class ControllerSeamStore : IRunLogStore
{
    public List<RunLogEvent> AppendedEvents { get; } = [];

    public int CompleteRunCalls { get; private set; }

    public int MarkRunAbandonedCalls { get; private set; }

    public RunLogSessionState? ResumeState { get; set; }

    public RunLogSessionState? TryResumeActiveRun() => ResumeState;

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
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
            ResumeState.State = entry.State;
            ResumeState.CurrentEncounterId = entry.EncounterId;
            ResumeState.LastStateFingerprint = entry.StateFingerprint;
            ResumeState.LastSelectionFingerprint = entry.SelectionFingerprint;
            if (entry.Kind == "selection_seen")
                ResumeState.PendingSelectionSeq = entry.Seq;
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
        }
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        CompleteRunCalls++;
        ResumeState = null;
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        MarkRunAbandonedCalls++;
        ResumeState = null;
    }
}
