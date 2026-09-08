#pragma warning disable CS0436
#nullable enable
using BazaarGameShared;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Lobby;

[HarmonyPatch(typeof(CosmeticsPanelController), "HandleButtonDimming")]
internal static class RandomHeroSkinPoolCategoryAccessPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref bool isRandomized) => isRandomized = false;
}

[HarmonyPatch(typeof(CosmeticsPanelController), "OnCosmeticPressed")]
internal static class RandomHeroSkinPoolCategorySelectionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        CosmeticsPanelController __instance,
        BazaarInventoryTypes.ECollectionType cosmeticType
    )
    {
        if (__instance._randomizeToggle == null || !__instance._randomizeToggle.isOn)
            return true;

        // Native category navigation disables randomization before opening the list. Keep the
        // same category/close behavior, without changing the player's random-loadout preference.
        __instance._lastSelectedType =
            __instance._lastSelectedType == cosmeticType
                ? BazaarInventoryTypes.ECollectionType.Invalid
                : cosmeticType;
        Events.LoadoutTypeChanged.Trigger(__instance._lastSelectedType);
        return false;
    }
}

[HarmonyPatch(typeof(LoadoutPopupController), "OnRandomizeToggled")]
internal static class RandomHeroSkinPoolKeepSubpanelPatch
{
    [HarmonyPrefix]
    private static bool Prefix(bool togglesEnabled) => !togglesEnabled;
}
