using System.Reflection;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus;

var trackerType = RequireType("BazaarPlusPlus.EncounterTracker");
var modStateType = RequireType("BazaarPlusPlus.ModState");

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

SetField(
    modStateType,
    "AvailableEncounters",
    new List<RunInfo.CardInfo> { new RunInfo.CardInfo() }
);
SetField(
    modStateType,
    "CurrentEncounterChoices",
    new List<RunInfo.CardInfo> { new RunInfo.CardInfo() }
);
SetField(
    modStateType,
    "EncounterMonsterPreviews",
    new List<RunInfo.MonsterPreview> { new RunInfo.MonsterPreview() }
);

resetMethod!.Invoke(null, ["test reset"]);

Assert(
    GetField(modStateType, "AvailableEncounters") == null,
    "Reset should clear available encounters."
);
Assert(
    GetField(modStateType, "CurrentEncounterChoices") == null,
    "Reset should clear current encounter choices."
);
Assert(
    GetField(modStateType, "EncounterMonsterPreviews") == null,
    "Reset should clear encounter monster previews."
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

static void SetField(Type type, string name, object value)
{
    var field = type.GetField(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
    );
    Assert(field != null, $"Field not found: {name}");
    field!.SetValue(null, value);
}

static object? GetField(Type type, string name)
{
    var field = type.GetField(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
    );
    Assert(field != null, $"Field not found: {name}");
    return field!.GetValue(null);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
