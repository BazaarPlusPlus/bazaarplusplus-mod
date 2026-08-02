#nullable enable
#pragma warning disable CS0436

using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using HarmonyLib;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch(typeof(ItemController), nameof(ItemController.Setup), [typeof(Card)])]
internal static class CombatReplayItemPresentationReadinessPatch
{
    [HarmonyPostfix]
    private static void Postfix(ItemController __instance, ref Task __result) =>
        __result = ReplayItemPresentationReadiness.Track(__instance, __result);
}
