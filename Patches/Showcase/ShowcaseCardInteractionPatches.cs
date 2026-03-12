#pragma warning disable CS0436
using HarmonyLib;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), "ProceedClick")]
internal static class ShowcaseCardClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardController __instance, PointerEventData eventData)
    {
        if (__instance == null || __instance.GetComponent<ShowcaseCardMarker>() == null)
            return true;

        return eventData != null && eventData.button == PointerEventData.InputButton.Right;
    }
}
