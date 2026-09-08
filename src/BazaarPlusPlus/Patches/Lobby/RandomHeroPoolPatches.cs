#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Game.Lobby.RandomHeroPool;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Menu;

namespace BazaarPlusPlus.Patches.Lobby;

[HarmonyPatch(typeof(HeroOptionController), "UpdateSelected")]
internal static class RandomHeroPoolProjectPatch
{
    [HarmonyPrefix]
    private static bool Prefix(HeroOptionController __instance)
    {
        if (!PlayerPreferences.Data.RandomHeroEnabled)
            return true;
        try
        {
            RandomHeroPoolNativeController.Project(__instance);
            return false;
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportHeroPoolDegraded(
                HeroPoolOperation.ProjectVisualUpdate,
                LobbyLogReasonCode.OperationException,
                ex
            );
            return true;
        }
    }
}

[HarmonyPatch(typeof(HeroOptionController), "HeroClicked")]
internal static class RandomHeroPoolClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(HeroOptionController __instance)
    {
        try
        {
            // Both true and false changes are user pool edits once group membership is removed.
            return !RandomHeroPoolNativeController.TryHandleClick(__instance);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportHeroPoolDegraded(
                HeroPoolOperation.RouteCardClick,
                LobbyLogReasonCode.OperationException,
                ex
            );
            return true;
        }
    }
}

[HarmonyPatch(typeof(HeroManager), nameof(HeroManager.SetRandomHero))]
internal static class RandomHeroPoolSelectPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            return !RandomHeroPoolNativeController.TrySelectRandomHero();
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportHeroPoolDegraded(
                HeroPoolOperation.RouteRandomSelection,
                LobbyLogReasonCode.OperationException,
                ex
            );
            return true;
        }
    }
}

[HarmonyPatch(typeof(HeroSelectPopupController), "OnHeroChanged")]
internal static class RandomHeroPoolKeepPopupPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !PlayerPreferences.Data.RandomHeroEnabled;
}
