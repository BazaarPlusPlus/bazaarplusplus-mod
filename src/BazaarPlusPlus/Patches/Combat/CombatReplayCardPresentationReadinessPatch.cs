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
        __result = ReplayCardPresentationReadiness.Track(__instance, __result);
}

[HarmonyPatch(typeof(SocketEffectController), "LoadVFXAssets")]
internal static class CombatReplaySocketEffectPresentationReadinessPatch
{
    // SocketEffectController.UpdateData is async void. Replacing the Task returned by its
    // awaited native loader is the only seam that lets replay bootstrap observe completion.
    [HarmonyPostfix]
    private static void Postfix(SocketEffectController __instance, ref Task __result) =>
        __result = ReplayCardPresentationReadiness.Track(__instance, __result);
}
