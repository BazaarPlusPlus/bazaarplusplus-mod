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
RequireType("BazaarPlusPlus.Core.Events.RunLifecycleChanged");
RequireType("BazaarPlusPlus.Core.Events.RunLoggingSyncRequested");
RequireType("BazaarPlusPlus.Core.Events.PvpBattleRecorded");
RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayModule");
RequireType("BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBarModule");
RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingModule");

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
Assert(
    !pluginSource.Contains("CombatStatusBar.InitializeConfig", StringComparison.Ordinal),
    "Plugin should not initialize CombatStatusBar config directly."
);

Assert(
    !File.Exists(
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../Models/ModState.cs")
        )
    ),
    "ModState compatibility shell should be removed."
);

var runtimeHostSource = ReadSource("Core/Runtime/BppRuntimeHost.cs");
Assert(
    runtimeHostSource.Contains("public static IBppConfig Config", StringComparison.Ordinal),
    "BppRuntimeHost should expose configuration through BppConfig."
);

var configInterfaceSource = ReadSource("Core/Config/IBppConfig.cs");
Assert(
    configInterfaceSource.Contains("EnableCombatStatusBarConfig", StringComparison.Ordinal),
    "IBppConfig should expose the CombatStatusBar enabled config."
);
Assert(
    configInterfaceSource.Contains("VisibleCombatStatusBarConfig", StringComparison.Ordinal),
    "IBppConfig should expose the CombatStatusBar visible config."
);
Assert(
    configInterfaceSource.Contains(
        "CombatStatusBarSpeedMultiplierConfig",
        StringComparison.Ordinal
    ),
    "IBppConfig should expose the CombatStatusBar speed config."
);

var configSource = ReadSource("Core/Config/BppConfig.cs");
Assert(
    configSource.Contains("EnableCombatStatusBarConfig", StringComparison.Ordinal),
    "BppConfig should bind the CombatStatusBar enabled config."
);
Assert(
    configSource.Contains("VisibleCombatStatusBarConfig", StringComparison.Ordinal),
    "BppConfig should bind the CombatStatusBar visible config."
);
Assert(
    configSource.Contains("CombatStatusBarSpeedMultiplierConfig", StringComparison.Ordinal),
    "BppConfig should bind the CombatStatusBar speed config."
);
Assert(
    runtimeHostSource.Contains("public static IRunContext RunContext", StringComparison.Ordinal),
    "BppRuntimeHost should expose run context through RunContextStore."
);
Assert(
    runtimeHostSource.Contains("public static IPathService Paths", StringComparison.Ordinal),
    "BppRuntimeHost should expose paths through BppPathService."
);
Assert(
    !runtimeHostSource.Contains("OnNetMessageObserved", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("OnCombatSimObserved", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("OnCombatFrameAdvanced", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("OnRunInitializedObserved", StringComparison.Ordinal),
    "BppRuntimeHost should compose feature modules instead of routing feature events itself."
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

var runLifecycleSource = ReadSource("Game/RunLifecycle/RunLifecycleModule.cs");
Assert(
    runLifecycleSource.Contains("Subscribe<RunInitializedObserved>", StringComparison.Ordinal),
    "RunLifecycleModule should consume run initialization through the event bus."
);
Assert(
    runLifecycleSource.Contains("_eventBus.Publish(", StringComparison.Ordinal)
        && runLifecycleSource.Contains("new RunLifecycleChanged", StringComparison.Ordinal),
    "RunLifecycleModule should publish lifecycle changes for dependent modules."
);
Assert(
    !runLifecycleSource.Contains("EncounterTracker.ResetEncounterState", StringComparison.Ordinal),
    "RunLifecycleModule should not reach into encounter tracking directly."
);

var encounterTrackerSource = ReadSource("Game/EncounterTracker.cs");
Assert(
    !encounterTrackerSource.Contains("RunLoggingController.Instance", StringComparison.Ordinal),
    "EncounterTracker should not call run logging directly."
);

var runStateSyncSource = ReadSource("Game/RunStateSyncController.cs");
Assert(
    runStateSyncSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal)
        && runStateSyncSource.Contains("new RunLoggingSyncRequested", StringComparison.Ordinal),
    "RunStateSyncController should publish run-logging sync requests instead of calling the controller directly."
);
Assert(
    !runStateSyncSource.Contains("RunLoggingController.Instance", StringComparison.Ordinal),
    "RunStateSyncController should not call RunLoggingController directly."
);

var runLoggingModuleSource = ReadSource("Game/RunLogging/RunLoggingModule.cs");
Assert(
    runLoggingModuleSource.Contains("Subscribe<SelectionObserved>", StringComparison.Ordinal)
        && runLoggingModuleSource.Contains(
            "Subscribe<RunLoggingSyncRequested>",
            StringComparison.Ordinal
        )
        && runLoggingModuleSource.Contains(
            "Subscribe<PvpBattleRecorded>",
            StringComparison.Ordinal
        ),
    "RunLoggingModule should be the unified subscriber for run-logging capture inputs."
);

Console.WriteLine("Architecture modularization smoke checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static string ReadSource(string relativePath)
{
    var sourcePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath)
    );
    Assert(File.Exists(sourcePath), $"Source file not found at {sourcePath}");
    return File.ReadAllText(sourcePath);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
