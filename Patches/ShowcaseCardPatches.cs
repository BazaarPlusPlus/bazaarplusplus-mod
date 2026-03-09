#pragma warning disable CS0436
using HarmonyLib;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), "ProceedClick")]
internal static class ShowcaseCardClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardController __instance)
    {
        return __instance == null || __instance.GetComponent<ShowcaseCardMarker>() == null;
    }
}

[HarmonyPatch(typeof(ItemController), nameof(ItemController.OnBeginDrag))]
internal static class ShowcaseCardDragPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemController __instance)
    {
        return __instance == null || __instance.GetComponent<ShowcaseCardMarker>() == null;
    }
}
