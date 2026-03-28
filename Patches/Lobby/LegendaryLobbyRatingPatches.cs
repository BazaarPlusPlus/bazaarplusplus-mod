#pragma warning disable CS0436
#nullable enable
using System;
using BazaarGameShared.TempoNet.Enums;
using BazaarPlusPlus.Game.Lobby;
using HarmonyLib;
using TMPro;
using TheBazaar;
using TheBazaar.ProfileData;
using TheBazaar.UI;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(MainMenuController), "OnProfileLoaded")]
internal static class LegendaryLobbyRatingOnProfileLoadedPatch
{
    [HarmonyPostfix]
    private static void Postfix(MainMenuController __instance, IProfile? plr)
    {
        if (plr == null)
            return;

        LegendaryLobbyRatingUi.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(MainMenuController), "OnCurrentSeasonRankDataUpdated")]
internal static class LegendaryLobbyRatingOnRankUpdatedPatch
{
    [HarmonyPostfix]
    private static void Postfix(MainMenuController __instance)
    {
        LegendaryLobbyRatingUi.Refresh(__instance);
    }
}

internal static class LegendaryLobbyRatingUi
{
    public static void Refresh(MainMenuController controller)
    {
        try
        {
            var heroBanner = Traverse.Create(controller).Field("HeroBanner").GetValue<HeroBannerController>();
            if (heroBanner == null)
                return;

            var leaderboardLabel = Traverse
                .Create(heroBanner)
                .Field("_leaderboardPositionLabel")
                .GetValue<TMP_Text>();
            if (leaderboardLabel == null)
                return;

            var currentSeasonRank = Data.Rank?.CurrentSeasonRank;
            if (currentSeasonRank == null)
                return;
            if (currentSeasonRank.Rank != ERank.Legendary)
                return;

            var leaderboardPosition = Data.LeaderboardProfile?.LeaderboardPosition;
            leaderboardLabel.text = LegendaryLobbyRatingFormatter.BuildLegendaryText(
                leaderboardPosition,
                currentSeasonRank.Rating
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn("LegendaryLobbyRating", $"Failed to refresh lobby rating text: {ex.Message}");
        }
    }
}
