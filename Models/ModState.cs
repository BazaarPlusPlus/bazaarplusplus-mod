#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace BazaarPlusPlus;

internal static class ModState
{
    public static ManualLogSource Logger;

    // Config entries
    public static ConfigEntry<bool> EnableNameOverrideConfig;
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
        EnableNameOverrideConfig = config.Bind(
            "StreamerMode",
            "EnableNameOverride",
            false,
            "Whether to replace the local player's displayed username"
        );
        BppLog.Info(
            "ModState",
            $"Configuration initialized: enableNameOverride={EnableNameOverrideConfig.Value}"
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

}
