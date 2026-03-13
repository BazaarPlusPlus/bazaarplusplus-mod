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
    public const bool IsDebug = true;
#else
    public const bool IsDebug = false;
#endif

    public static ManualLogSource Logger;

    // Config entries
    public static ConfigEntry<bool> EnableNameOverrideConfig;
    public static ConfigEntry<bool> PromoteDebugLogsToInfoConfig;
    public static ConfigEntry<int> LogRepeatPatternMaxLengthConfig;
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
            "Whether to replace the local player's displayed username"
        );
        PromoteDebugLogsToInfoConfig = config.Bind(
            "Logging",
            "PromoteDebugLogsToInfo",
            false,
            "When enabled, BppLog.Debug entries are emitted at Info level. Useful during development when Debug logs are filtered out."
        );
        LogRepeatPatternMaxLengthConfig = config.Bind(
            "Logging",
            "RepeatDetectionMaxLength",
            3,
            "Maximum repeated log sequence length to coalesce. 1 only coalesces identical consecutive lines; 2 or more also coalesces repeating blocks such as A B A B."
        );
        BppLog.Info(
            "ModState",
            $"Configuration initialized: enableNameOverride={EnableNameOverrideConfig.Value}, promoteDebugLogsToInfo={PromoteDebugLogsToInfoConfig.Value}, repeatDetectionMaxLength={LogRepeatPatternMaxLengthConfig.Value}"
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
            BppLog.Debug("ModState", $"cards.json path initialized: {CardsJsonPath}");
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
        BppLog.Debug("ModState", "Run started; IsInGameRun=true");
    }

    private static void OnRunEnded()
    {
        IsInGameRun = false;
        BppLog.Debug("ModState", "Run ended; IsInGameRun=false");
    }

    private static void OnRunInterrupted()
    {
        IsInGameRun = false;
        BppLog.Debug("ModState", "Run interrupted; IsInGameRun=false");
    }
}
