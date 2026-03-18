#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using BepInEx.Configuration;
using BepInEx.Logging;
using TheBazaar;

namespace BazaarPlusPlus;

internal static class ModState
{
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
    public static ManualLogSource Logger;

    // Config entries
    public static ConfigEntry<bool> EnableNameOverrideConfig;
    public static ConfigEntry<bool> EnchantPreviewAlwaysShowConfig;
    public static ConfigEntry<string> EnchantPreviewHotkeyPathConfig;
    public static ConfigEntry<string> UpgradePreviewHotkeyPathConfig;

    // Game state
    public static bool IsInGameRun;
    public static string CurrentServerRunId;
    public static RunExitKind LastRunExitKind;
    public static EVictoryCondition LastVictoryCondition;
    public static string LastMessageId = "";
    public static DateTime LastSentTime = DateTime.MinValue;
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(2);

    // Encounter selection tracking
    public static List<RunInfo.CardInfo> AvailableEncounters; // map path options (ERunState.Encounter)
    public static List<RunInfo.CardInfo> CurrentEncounterChoices; // choices inside an encounter (Choice/Loot/Pedestal)
    public static List<RunInfo.MonsterPreview> EncounterMonsterPreviews; // combat encounter monster info from local DB

    // Paths for local data
    public static string CardsJsonPath;
    public static string RunLogDatabasePath;
    public static string CombatReplayDirectoryPath;

    public static void Initialize(ConfigFile config)
    {
        IsInGameRun = false;
        CurrentServerRunId = null;
        LastRunExitKind = RunExitKind.Completed;
        EnableNameOverrideConfig = config.Bind(
            "StreamerMode",
            "EnableNameOverride",
            false,
            "Whether to set the in-game display name to Anonymous"
        );
        EnchantPreviewAlwaysShowConfig = config.Bind(
            "EnchantPreview",
            "AlwaysShow",
            true,
            "Whether to always show enchant preview text in item tooltips. If disabled, hold Ctrl to show it."
        );
        EnchantPreviewHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "EnchantPreview",
            "<Keyboard>/ctrl",
            "Binding path for enchant preview tooltip mode."
        );
        UpgradePreviewHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "UpgradePreview",
            "<Keyboard>/shift",
            "Binding path for upgrade preview tooltip mode."
        );
        BppLog.Info(
            "ModState",
            $"Configuration initialized: enableNameOverride={EnableNameOverrideConfig.Value}, enchantPreviewAlwaysShow={EnchantPreviewAlwaysShowConfig.Value}, enchantPreviewHotkey={EnchantPreviewHotkeyPathConfig.Value}, upgradePreviewHotkey={UpgradePreviewHotkeyPathConfig.Value}"
        );
        CardsJsonPath = CardJsonPathResolver.GetCardsJsonPath();
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

        RunLogDatabasePath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            RunLogSqliteSchema.DatabaseFileName
        );
        CombatReplayDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            "CombatReplays"
        );
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
        if (Data.IsInCombat)
            return true;

        var currentAppState = AppState.CurrentState;
        if (currentAppState is RunAppState)
            return !currentAppState.IsEndOfRunState();

        if (currentAppState is ReplayState)
            return true;

        // Do not fall back to Data.HasActiveRun/Data.CurrentState when there is no active app state.
        // Those values can linger briefly after returning to the lobby and incorrectly mark menu screens as in-run.
        return currentAppState is StartRunAppState;
    }
}
