#pragma warning disable CS0436
using System.Threading;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

// Combat sim: capture win/loss result
[HarmonyPatch(typeof(CombatSimHandler), "Simulate")]
class CombatSimPatch
{
    [HarmonyPrefix]
    static void Prefix(NetMessageCombatSim message, CancellationTokenSource cancellationToken)
    {
        if (ModState.LastMessageId == message.MessageId)
            return;
        ModState.LastMessageId = message.MessageId;
        ModState.LastVictoryCondition =
            message.Data.Winner == ECombatantId.Player
                ? EVictoryCondition.Win
                : EVictoryCondition.Lose;
    }
}

// Streamer mode: replace in-game username with display name
[HarmonyPatch(typeof(HeroBannerController), "UpdatePlayer")]
public static class UpdatePlayerPatch
{
    [HarmonyPrefix]
    static bool Prefix(
        HeroBannerController __instance,
        ref string userName,
        ref int nameId,
        ref string titlePrefix,
        TheBazaar.ProfileData.ISeasonRank currentSeasonRank,
        int? leaderboardPosition
    )
    {
        if (userName != Data.Profile?.Username)
            return true;
        if (string.IsNullOrEmpty(ModState.UidConfig.Value))
            return true;

        userName = ModState.DisplayNameConfig.Value;
        nameId = 0;
        return true;
    }
}

[HarmonyPatch(typeof(HeroBannerController), "SetHeroName")]
public static class SetHeroNamePatch
{
    [HarmonyPrefix]
    static bool Prefix(ref string newName, ref int usernameId)
    {
        if (newName != Data.Profile?.Username)
            return true;
        if (string.IsNullOrEmpty(ModState.UidConfig.Value))
            return true;

        newName = ModState.DisplayNameConfig.Value;
        usernameId = 0;
        return true;
    }
}
