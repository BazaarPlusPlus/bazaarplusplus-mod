using System.Reflection;

RequireType("BazaarPlusPlus.Core.Runtime.BppRuntimeHost");
RequireType("BazaarPlusPlus.Core.Events.IBppEventBus");
RequireType("BazaarPlusPlus.Core.Events.InMemoryBppEventBus");
RequireType("BazaarPlusPlus.Core.Config.IBppConfig");
RequireType("BazaarPlusPlus.Core.Paths.IPathService");
RequireType("BazaarPlusPlus.Core.RunContext.IRunContext");
RequireType("BazaarPlusPlus.Core.GameState.IGameStateProbe");
RequireType("BazaarPlusPlus.Core.Config.BppConfig");
RequireType("BazaarPlusPlus.Core.Paths.BppPathService");
RequireType("BazaarPlusPlus.Core.RunContext.RunContextStore");
RequireType("BazaarPlusPlus.Core.GameState.GameStateProbe");
RequireType("BazaarPlusPlus.Core.Events.RunInitializedObserved");
RequireType("BazaarPlusPlus.Core.Events.NetMessageObserved");
RequireType("BazaarPlusPlus.Core.Events.CombatSimObserved");
RequireType("BazaarPlusPlus.Core.Events.CombatFrameAdvanced");

var pluginSource = ReadSource("Plugin.cs");
Assert(
    pluginSource.Contains("BppRuntimeHost", StringComparison.Ordinal),
    "Plugin should reference BppRuntimeHost."
);
Assert(
    pluginSource.Contains("_runtimeHost.Install();", StringComparison.Ordinal),
    "Plugin should install the runtime host."
);
Assert(
    pluginSource.Contains("_runtimeHost.Start();", StringComparison.Ordinal),
    "Plugin should start the runtime host."
);

var modStateSource = ReadSource("Models/ModState.cs");
Assert(
    modStateSource.Contains("BppConfig", StringComparison.Ordinal),
    "ModState should delegate config concerns through BppConfig."
);
Assert(
    modStateSource.Contains("RunContextStore", StringComparison.Ordinal),
    "ModState should delegate run context through RunContextStore."
);
Assert(
    modStateSource.Contains("BppPathService", StringComparison.Ordinal),
    "ModState should delegate path concerns through BppPathService."
);

var logSource = ReadSource("Infrastructure/BppLog.cs");
Assert(
    logSource.Contains("Logger =>", StringComparison.Ordinal),
    "BppLog should resolve the logger through an accessor instead of raw state writes."
);

var runInitializedPatchSource = ReadSource("Patches/RunLogging/RunInitializedPatch.cs");
Assert(
    runInitializedPatchSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal),
    "RunInitializedPatch should publish through the event bus."
);
Assert(
    !runInitializedPatchSource.Contains("ModState.CurrentServerRunId", StringComparison.Ordinal),
    "RunInitializedPatch should not write ModState directly."
);

var replayPatchSource = ReadSource("Patches/Combat/CombatReplayCapturePatch.cs");
Assert(
    replayPatchSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal),
    "CombatReplayCapturePatch should publish observed net messages through the event bus."
);
Assert(
    !replayPatchSource.Contains("CombatReplayRuntime.Instance", StringComparison.Ordinal),
    "CombatReplayCapturePatch should no longer call CombatReplayRuntime.Instance directly."
);

var combatPatchSource = ReadSource("Patches/Combat/CombatSimulationPatches.cs");
Assert(
    combatPatchSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal),
    "Combat simulation patches should publish combat events through the event bus."
);
Assert(
    !combatPatchSource.Contains("CombatStatusBar.SetCombatFrameTotal", StringComparison.Ordinal),
    "Combat simulation patches should not call CombatStatusBar state methods directly."
);
Assert(
    !combatPatchSource.Contains("CombatStatusBar.AdvanceCombatFrame", StringComparison.Ordinal),
    "Combat frame advance patch should not call CombatStatusBar directly."
);

Console.WriteLine("Architecture modularization smoke checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static string ReadSource(string relativePath)
{
    var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));
    Assert(File.Exists(sourcePath), $"Source file not found at {sourcePath}");
    return File.ReadAllText(sourcePath);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
