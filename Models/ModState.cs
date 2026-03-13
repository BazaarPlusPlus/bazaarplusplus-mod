#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BepInEx.Configuration;
using BepInEx.Logging;
using TheBazaar;

namespace BazaarPlusPlus;

internal static class ModState
{
#if DEBUG
    public static readonly bool IsDebug = true;
#else
    public static readonly bool IsDebug = false;
#endif

    public static ManualLogSource Logger;

    // Config entries
    public static ConfigEntry<bool> EnableNameOverrideConfig;
    public static ConfigEntry<bool> EnchantPreviewAlwaysShowConfig;
    public static bool IsInGameRun;
    public static EVictoryCondition LastVictoryCondition;
    public static string LastMessageId = "";
    public static DateTime LastSentTime = DateTime.MinValue;
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(2);

    // Encounter selection tracking
    public static List<RunInfo.CardInfo> AvailableEncounters; // map path options (ERunState.Encounter)
    public static List<RunInfo.CardInfo> CurrentEncounterChoices; // choices inside an encounter (Choice/Loot/Pedestal)
    public static List<RunInfo.MonsterPreview> EncounterMonsterPreviews; // combat encounter monster info from local DB

    public static List<EEnchantmentType> AvailableEnchantments = new List<EEnchantmentType>();
    public static string CardsJsonPath;

    public static void Initialize(ConfigFile config)
    {
        IsInGameRun = false;
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
        BppLog.Info(
            "ModState",
            $"Configuration initialized: enableNameOverride={EnableNameOverrideConfig.Value}, enchantPreviewAlwaysShow={EnchantPreviewAlwaysShowConfig.Value}"
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
    }

    public static void Subscribe()
    {
        Events.RunStarted.AddListener(OnRunStarted, null);
        Events.RunEnded.AddListener(OnRunEnded, null);
        Events.RunInterrupted.AddListener(OnRunInterrupted, null);
        BppLog.Info("ModState", "Subscribed to run lifecycle events");
    }

    private static void OnRunStarted()
    {
        IsInGameRun = true;
        EncounterTracker.ResetEncounterState("Run started");
        BppLog.Debug("ModState", "Run started; IsInGameRun=true");
    }

    private static void OnRunEnded()
    {
        IsInGameRun = false;
        EncounterTracker.ResetEncounterState("Run ended");
        BppLog.Debug("ModState", "Run ended; IsInGameRun=false");
    }

    private static void OnRunInterrupted()
    {
        IsInGameRun = false;
        EncounterTracker.ResetEncounterState("Run interrupted");
        BppLog.Debug("ModState", "Run interrupted; IsInGameRun=false");
    }
}
