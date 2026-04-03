#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.RunLogging.Models;

var captureServiceType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogCaptureService");
var pvpBattleInputType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogPvpBattleInput");
var controllerCoreType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingControllerCore");
var manifestType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifest");
var payloadType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload");
var cardSetCaptureType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSetCapture");

Assert(manifestType.GetProperty("BattleId") != null, "Manifest should expose BattleId.");
Assert(manifestType.GetProperty("ReplayId") == null, "Manifest should not expose ReplayId.");
Assert(payloadType.GetProperty("BattleId") != null, "Replay payload should expose BattleId.");
Assert(
    cardSetCaptureType.GetProperty("Items") != null
        && cardSetCaptureType.GetProperty("Status") != null
        && cardSetCaptureType.GetProperty("Source") != null,
    "PvpBattleCardSetCapture should expose Items, Status, and Source."
);

var service =
    Activator.CreateInstance(captureServiceType)
    ?? throw new InvalidOperationException("RunLogCaptureService should be constructible.");

Assert(
    Type.GetType("BazaarPlusPlus.Game.RunLogging.RunLogStateSnapshotInput, BazaarPlusPlus") == null,
    "RunLogStateSnapshotInput should be removed."
);
Assert(
    Type.GetType("BazaarPlusPlus.Game.RunLogging.RunLogRunProgressInput, BazaarPlusPlus") == null,
    "RunLogRunProgressInput should be removed."
);
Assert(
    Type.GetType("BazaarPlusPlus.Game.RunLogging.RunLogSelectionSnapshotInput, BazaarPlusPlus")
        == null,
    "RunLogSelectionSnapshotInput should be removed."
);
Assert(
    Type.GetType("BazaarPlusPlus.Game.RunLogging.RunLogSelectionOptionInput, BazaarPlusPlus")
        == null,
    "RunLogSelectionOptionInput should be removed."
);
Assert(
    captureServiceType.GetMethod(
        "BuildRunProgressEvent",
        BindingFlags.Public | BindingFlags.Instance
    ) == null,
    "RunLogCaptureService should no longer expose BuildRunProgressEvent."
);
Assert(
    captureServiceType.GetMethod(
        "BuildSelectionSeenEvent",
        BindingFlags.Public | BindingFlags.Instance
    ) == null,
    "RunLogCaptureService should no longer expose BuildSelectionSeenEvent."
);
Assert(
    captureServiceType.GetMethod("BuildStateSeenEvent", BindingFlags.Public | BindingFlags.Instance)
        == null,
    "RunLogCaptureService should no longer expose BuildStateSeenEvent."
);
Assert(
    controllerCoreType.GetMethod("AcceptRunProgress", BindingFlags.Public | BindingFlags.Instance)
        == null,
    "RunLoggingControllerCore should no longer expose AcceptRunProgress."
);
Assert(
    controllerCoreType.GetMethod("AcceptStateSnapshot", BindingFlags.Public | BindingFlags.Instance)
        == null,
    "RunLoggingControllerCore should no longer expose AcceptStateSnapshot."
);
Assert(
    controllerCoreType.GetMethod(
        "AcceptSelectionSnapshot",
        BindingFlags.Public | BindingFlags.Instance
    ) == null,
    "RunLoggingControllerCore should no longer expose AcceptSelectionSnapshot."
);
Assert(
    controllerCoreType.GetMethod("AcceptChoiceMade", BindingFlags.Public | BindingFlags.Instance)
        == null,
    "RunLoggingControllerCore should no longer expose AcceptChoiceMade."
);
Assert(
    controllerCoreType.GetMethod(
        "AcceptSelectionAbandoned",
        BindingFlags.Public | BindingFlags.Instance
    ) == null,
    "RunLoggingControllerCore should no longer expose AcceptSelectionAbandoned."
);

var pvpBattleInput =
    Activator.CreateInstance(pvpBattleInputType)
    ?? throw new InvalidOperationException("RunLogPvpBattleInput should be constructible.");
SetProperty(pvpBattleInputType, pvpBattleInput, "BattleId", "battle-123");
SetProperty(pvpBattleInputType, pvpBattleInput, "CombatKind", "PVPCombat");
SetProperty(pvpBattleInputType, pvpBattleInput, "Day", 4);
SetProperty(pvpBattleInputType, pvpBattleInput, "Hour", 6);
SetProperty(pvpBattleInputType, pvpBattleInput, "EncounterId", "encounter-pvp-1");
SetProperty(pvpBattleInputType, pvpBattleInput, "OpponentName", "Rival");

var combatReplayEvent = Invoke<RunLogEvent>(
    captureServiceType,
    service,
    "BuildPvpBattleRecordedEvent",
    [pvpBattleInput]
);
Assert(
    combatReplayEvent.Kind == "pvp_combat_recorded",
    "Combat replays should map to a pvp_combat_recorded event."
);
Assert(
    combatReplayEvent.CombatKind == "PVPCombat",
    "Combat replay events should preserve the combat kind."
);
Assert(
    combatReplayEvent.BattleId == "battle-123",
    "Combat replay events should preserve the battle id."
);
Assert(
    combatReplayEvent.OpponentName == "Rival",
    "Combat replay events should preserve opponent metadata."
);

Console.WriteLine("RunLogging capture checks passed.");

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
