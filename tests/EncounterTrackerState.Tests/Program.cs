using System.Reflection;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus;

var trackerType = RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterTracker");
var featureType = RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterTrackingFeature");
var controllerType = RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterTrackingController");
var moduleType = RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterTrackingModule");
var queryType = RequireType("BazaarPlusPlus.Game.EncounterTracking.IEncounterSelectionQuery");
var snapshotType = RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterSelectionSnapshot");
var bppFeatureType = RequireType("BazaarPlusPlus.Core.Runtime.IBppFeature");
RequireType("BazaarPlusPlus.Core.Events.SelectionObserved");
RequireType("BazaarPlusPlus.Core.Events.RunLifecycleChanged");
Assert(
    bppFeatureType.IsAssignableFrom(featureType),
    "EncounterTrackingFeature should implement IBppFeature."
);
Assert(
    controllerType.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public) != null
        && controllerType.GetMethod("Stop", BindingFlags.Instance | BindingFlags.Public) != null,
    "EncounterTrackingController should expose Start/Stop lifecycle hooks."
);

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
var initializeMethod = trackerType.GetMethod(
    "Initialize",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(initializeMethod != null, "EncounterTracker should support explicit initialization.");
var eventBusType = RequireType("BazaarPlusPlus.Core.Events.InMemoryBppEventBus");
var eventBus = Activator.CreateInstance(eventBusType);
var runContextType = RequireType("BazaarPlusPlus.Core.RunContext.RunContextStore");
var runContext = Activator.CreateInstance(runContextType);
var monsterCatalog = featureType.Assembly.GetType("BazaarPlusPlus.Core.Runtime.BppRuntimeHost+EmptyMonsterCatalog")
    ?? throw new InvalidOperationException("EmptyMonsterCatalog not found.");
initializeMethod!.Invoke(null, [eventBus, runContext, Activator.CreateInstance(monsterCatalog, nonPublic: true)]);

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
var detachMethod = trackerType.GetMethod(
    "DetachFeature",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    detachMethod != null,
    "EncounterTracker should expose a detach path to avoid compatibility-shell feature leaks."
);

var trackerSource = ReadSource("Game/EncounterTracker.cs");
Assert(
    !trackerSource.Contains("OnCardDealt(", StringComparison.Ordinal)
        && !trackerSource.Contains("Events.CardDealtSimEvent.AddListener", StringComparison.Ordinal)
        && !trackerSource.Contains("BuildMonsterPreviews(", StringComparison.Ordinal),
    "EncounterTracker should be a compatibility shell and should not own card-deal orchestration."
);

var featureQueryProperty = featureType.GetProperty(
    "SelectionQuery",
    BindingFlags.Instance | BindingFlags.Public
);
Assert(
    featureQueryProperty != null && queryType.IsAssignableFrom(featureQueryProperty.PropertyType),
    "EncounterTrackingFeature should expose SelectionQuery."
);

var module = Activator.CreateInstance(moduleType, [eventBus]);
Assert(module != null, "EncounterTrackingFeature module should be available.");
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

var moduleQuery = moduleType.GetProperty("Query", BindingFlags.Instance | BindingFlags.Public)!
    .GetValue(module);
var seededSnapshot = queryType.GetMethod("GetSnapshot")!.Invoke(moduleQuery, Array.Empty<object>());
Assert(
    snapshotType.GetProperty("AvailableEncounters")!.GetValue(seededSnapshot) != null,
    "EncounterTrackingModule update should populate available encounters."
);

var clearMethod = moduleType.GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public);
Assert(clearMethod != null, "EncounterTrackingModule should expose Clear().");
clearMethod!.Invoke(module, Array.Empty<object>());
var clearedModuleSnapshot = queryType.GetMethod("GetSnapshot")!.Invoke(moduleQuery, Array.Empty<object>());
Assert(
    snapshotType.GetProperty("AvailableEncounters")!.GetValue(clearedModuleSnapshot) == null,
    "EncounterTrackingModule Clear should remove available encounters."
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

detachMethod!.Invoke(null, Array.Empty<object>());
Assert(
    Throws<InvalidOperationException>(() => queryProperty.GetValue(null)),
    "EncounterTracker should detach feature reference during teardown."
);

Console.WriteLine("EncounterTrackerState checks passed.");

bool Supports(ERunState state)
{
    return (bool)stateMethod!.Invoke(null, [state])!;
}

static Type RequireType(string fullName)
{
    var assembly = Assembly.Load("BazaarPlusPlus");
    return assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static string ReadSource(string relativePath)
{
    var sourcePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath)
    );
    Assert(File.Exists(sourcePath), $"Source file not found at {sourcePath}");
    return File.ReadAllText(sourcePath);
}

static bool Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
        return false;
    }
    catch (TException)
    {
        return true;
    }
    catch (TargetInvocationException ex) when (ex.InnerException is TException)
    {
        return true;
    }
}
