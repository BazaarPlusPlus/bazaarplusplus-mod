using System.Reflection;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus;

var trackerType = RequireType("BazaarPlusPlus.EncounterTracker");
var moduleType = RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterTrackingModule");
var queryType = RequireType("BazaarPlusPlus.Game.EncounterTracking.IEncounterSelectionQuery");
var snapshotType = RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterSelectionSnapshot");
RequireType("BazaarPlusPlus.Core.Events.SelectionObserved");
RequireType("BazaarPlusPlus.Core.Events.RunLifecycleChanged");

Assert(
    snapshotType.GetProperty("AvailableEncounters") != null
        && snapshotType.GetProperty("CurrentEncounterChoices") != null
        && snapshotType.GetProperty("EncounterMonsterPreviews") != null,
    "EncounterSelectionSnapshot should expose encounter lists and monster previews."
);

var queryProperty = trackerType.GetProperty(
    "SelectionQuery",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    queryProperty != null && queryType.IsAssignableFrom(queryProperty.PropertyType),
    "EncounterTracker should expose a read-only encounter selection query."
);

var stateMethod = trackerType.GetMethod(
    "IsSupportedSelectionState",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    stateMethod != null,
    "EncounterTracker should expose IsSupportedSelectionState for focused state gating tests."
);

Assert(Supports(ERunState.Encounter), "Encounter state should be supported.");
Assert(Supports(ERunState.Choice), "Choice state should be supported.");
Assert(Supports(ERunState.Loot), "Loot state should be supported.");
Assert(Supports(ERunState.Pedestal), "Pedestal state should be supported.");
Assert(!Supports(ERunState.Combat), "Combat state should not be supported.");
Assert(!Supports(ERunState.PVPCombat), "PVP combat state should not be supported.");
Assert(!Supports(ERunState.LevelUp), "LevelUp state should not be supported.");

var resetMethod = trackerType.GetMethod(
    "ResetEncounterState",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    resetMethod != null,
    "EncounterTracker should expose ResetEncounterState so lifecycle code can clear stale cache."
);

var moduleField = trackerType.GetField("Module", BindingFlags.NonPublic | BindingFlags.Static);
Assert(moduleField != null, "EncounterTracker should keep a module instance.");
var initializeMethod = trackerType.GetMethod(
    "Initialize",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(initializeMethod != null, "EncounterTracker should support explicit initialization.");
var eventBusType = RequireType("BazaarPlusPlus.Core.Events.InMemoryBppEventBus");
var eventBus = Activator.CreateInstance(eventBusType);
initializeMethod!.Invoke(null, [eventBus]);
var module = moduleField!.GetValue(null);
Assert(module != null, "Encounter tracking module should be available.");
var updateSelectionMethod = moduleType.GetMethod(
    "UpdateSelection",
    BindingFlags.Instance | BindingFlags.Public
);
Assert(
    updateSelectionMethod != null,
    "Encounter tracking module should support selection updates."
);
updateSelectionMethod!.Invoke(
    module,
    [
        ERunState.Encounter,
        new List<RunInfo.CardInfo> { new RunInfo.CardInfo() },
        new List<RunInfo.MonsterPreview> { new RunInfo.MonsterPreview() },
    ]
);

resetMethod!.Invoke(null, ["test reset"]);

var selectionQuery = queryProperty!.GetValue(null);
var snapshot = queryType.GetMethod("GetSnapshot")!.Invoke(selectionQuery, Array.Empty<object>());
Assert(snapshot != null, "Encounter selection query should return a snapshot object.");
Assert(
    snapshotType.GetProperty("AvailableEncounters")!.GetValue(snapshot) == null,
    "Reset should clear the module-owned available encounters snapshot."
);
Assert(
    snapshotType.GetProperty("CurrentEncounterChoices")!.GetValue(snapshot) == null,
    "Reset should clear the module-owned current encounter choices snapshot."
);
Assert(
    snapshotType.GetProperty("EncounterMonsterPreviews")!.GetValue(snapshot) == null,
    "Reset should clear the module-owned encounter monster previews snapshot."
);

Console.WriteLine("EncounterTrackerState checks passed.");

bool Supports(ERunState state)
{
    return (bool)stateMethod!.Invoke(null, [state])!;
}

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
