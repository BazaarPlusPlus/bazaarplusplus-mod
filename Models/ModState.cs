#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using BepInEx.Configuration;
using BepInEx.Logging;
using TheBazaar;

namespace BazaarPlusPlus;

internal static class ModState
{
    internal static readonly BppConfig Config = new();
    internal static readonly BppPathService Paths = new();
    internal static readonly RunContextStore RunContext = new();
    internal static readonly GameStateProbe GameStateProbe = new();

    internal enum RunExitKind
    {
        Completed,
        Interrupted,
    }

#if DEBUG
    public static readonly bool IsDebug = true;
#else
    public static readonly bool IsDebug = false;
#endif

    // Logging
    public static ManualLogSource? Logger;

    // Config entries
    public static ConfigEntry<bool>? EnableNameOverrideConfig => Config.EnableNameOverrideConfig;
    public static ConfigEntry<bool>? EnchantPreviewAlwaysShowConfig =>
        Config.EnchantPreviewAlwaysShowConfig;
    public static ConfigEntry<string>? EnchantPreviewHotkeyPathConfig =>
        Config.EnchantPreviewHotkeyPathConfig;
    public static ConfigEntry<string>? UpgradePreviewHotkeyPathConfig =>
        Config.UpgradePreviewHotkeyPathConfig;

    // Game state
    public static bool IsInGameRun
    {
        get => RunContext.IsInGameRun;
        set => RunContext.IsInGameRun = value;
    }

    public static string? CurrentServerRunId
    {
        get => RunContext.CurrentServerRunId;
        set => RunContext.CurrentServerRunId = value;
    }

    public static RunExitKind LastRunExitKind;
    public static EVictoryCondition LastVictoryCondition;
    public static string LastMessageId
    {
        get => RunContext.LastMessageId;
        set => RunContext.LastMessageId = value;
    }

    public static DateTime LastSentTime = DateTime.MinValue;
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(2);

    // Encounter selection tracking
    public static List<RunInfo.CardInfo>? AvailableEncounters; // map path options (ERunState.Encounter)
    public static List<RunInfo.CardInfo>? CurrentEncounterChoices; // choices inside an encounter (Choice/Loot/Pedestal)
    public static List<RunInfo.MonsterPreview>? EncounterMonsterPreviews; // combat encounter monster info from local DB

    // Paths for local data
    public static string? CardsJsonPath => Paths.CardsJsonPath;
    public static string? RunLogDatabasePath => Paths.RunLogDatabasePath;
    public static string? CombatReplayDirectoryPath => Paths.CombatReplayDirectoryPath;

    public static void Initialize(ConfigFile config)
    {
        RunContext.Reset();
        LastRunExitKind = RunExitKind.Completed;
        Config.Initialize(config);
        var enableNameOverride = EnableNameOverrideConfig?.Value;
        var enchantPreviewAlwaysShow = EnchantPreviewAlwaysShowConfig?.Value;
        var enchantPreviewHotkey = EnchantPreviewHotkeyPathConfig?.Value;
        var upgradePreviewHotkey = UpgradePreviewHotkeyPathConfig?.Value;
        BppLog.Info(
            "ModState",
            $"Configuration initialized: enableNameOverride={enableNameOverride}, enchantPreviewAlwaysShow={enchantPreviewAlwaysShow}, enchantPreviewHotkey={enchantPreviewHotkey}, upgradePreviewHotkey={upgradePreviewHotkey}"
        );
        Paths.Initialize();
        if (string.IsNullOrWhiteSpace(CardsJsonPath))
        {
            BppLog.Error(
                "ModState",
                "Failed to resolve cards.json path from BepInEx game root; cards.json will be unavailable"
            );
        }
        else
        {
            BppLog.Info("ModState", $"cards.json path initialized: {CardsJsonPath}");
        }
    }

    public static void Subscribe()
    {
        Events.RunStarted.AddListener(OnRunStarted, null);
        Events.RunEnded.AddListener(OnRunEnded, null);
        Events.RunInterrupted.AddListener(OnRunInterrupted, null);
        BppLog.Info("ModState", "Subscribed to run lifecycle events");
    }

    public static void RefreshRunStateFromCurrentState()
    {
        SetInGameRun(ComputeIsInGameRun(), "Live run-state reconciliation");
    }

    private static void OnRunStarted()
    {
        EncounterTracker.ResetEncounterState("Run started");
        LastRunExitKind = RunExitKind.Completed;
        SetInGameRun(true, "Run started");
    }

    private static void OnRunEnded()
    {
        CurrentServerRunId = null;
        LastRunExitKind = RunExitKind.Completed;
        SetInGameRun(false, "Run ended");
    }

    private static void OnRunInterrupted()
    {
        CurrentServerRunId = null;
        LastRunExitKind = RunExitKind.Interrupted;
        SetInGameRun(false, "Run interrupted");
    }

    private static void SetInGameRun(bool inGameRun, string reason)
    {
        if (IsInGameRun == inGameRun)
            return;

        IsInGameRun = inGameRun;
        if (!inGameRun)
            EncounterTracker.ResetEncounterState(reason);

        BppLog.Debug(
            "ModState",
            $"{reason}; IsInGameRun={IsInGameRun}, appState={AppState.CurrentState?.GetType().Name ?? "null"}, runState={Data.CurrentState?.StateName.ToString() ?? "null"}, hasActiveRun={Data.HasActiveRun}"
        );
    }

    private static bool ComputeIsInGameRun()
    {
        return GameStateProbe.ComputeIsInGameRun();
    }
}
