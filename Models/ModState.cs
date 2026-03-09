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
    public static ConfigEntry<string> UidConfig;
    public static ConfigEntry<string> DisplayNameConfig;

    // Runtime state
    public static string RunId;
    public static EVictoryCondition LastVictoryCondition;
    public static string LastMessageId = "";
    public static DateTime LastSentTime = DateTime.MinValue;
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(2);

    // Encounter selection tracking
    public static List<RunInfo.CardInfo> AvailableEncounters;    // map path options (ERunState.Encounter)
    public static List<RunInfo.CardInfo> CurrentEncounterChoices; // choices inside an encounter (Choice/Loot/Pedestal)

    // Item tag data
    public static Dictionary<string, List<string>> BaseItemTags;

    public static void Initialize(ConfigFile config)
    {
        UidConfig = config.Bind("Authentication", "Uid", "", "Firebase User ID");
        DisplayNameConfig = config.Bind("Authentication", "DisplayName", "", "Display Name");
    }
}
