#nullable enable
#pragma warning disable CS0436
using BazaarGameClient.Domain.Models.Cards;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.PostCombatImpact;

[HarmonyPatch(typeof(RecapItemVisualController), nameof(RecapItemVisualController.OnPointerEnter))]
internal static class PostCombatImpactRecapPointerEnterPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        RecapItemVisualController __instance,
        Card ___CardData,
        CardTooltipData? ___cardTooltipData,
        Vector3 ___tooltipOffset
    )
    {
        if (___CardData == null)
            return;

        BppPatchHost.Features.PostCombatImpact.SetHoveredRecapCard(
            __instance,
            ___CardData,
            ___cardTooltipData,
            ___tooltipOffset
        );
    }
}

[HarmonyPatch(typeof(RecapItemVisualController), nameof(RecapItemVisualController.OnPointerExit))]
internal static class PostCombatImpactRecapPointerExitPatch
{
    [HarmonyPrefix]
    private static void Prefix(RecapItemVisualController __instance) =>
        BppPatchHost.Features.PostCombatImpact.ClearHoveredRecapCard(__instance);
}

[HarmonyPatch(typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerEnter))]
internal static class PostCombatImpactSkillPointerEnterPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        SkillProxyRenderer __instance,
        CardTooltipData? ____tooltipData,
        Vector3 ____tooltipOffsetWorldSpace
    )
    {
        var boardManager = Singleton<BoardManager>.Instance;
        var card = __instance.Card;
        if (card == null || boardManager == null || !boardManager.IsRecapViewOpen)
            return;

        BppPatchHost.Features.PostCombatImpact.SetHoveredSkill(
            __instance,
            card,
            ____tooltipData,
            ____tooltipOffsetWorldSpace
        );
    }
}

[HarmonyPatch(typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerExit))]
internal static class PostCombatImpactSkillPointerExitPatch
{
    [HarmonyPrefix]
    private static void Prefix(SkillProxyRenderer __instance) =>
        BppPatchHost.Features.PostCombatImpact.ClearHoveredSkill(__instance);
}

[HarmonyPatch(
    typeof(AuxiliaryTooltipController),
    nameof(AuxiliaryTooltipController.ShowAuxiliaryTooltipController)
)]
internal static class PostCombatImpactAuxiliaryTooltipShowPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        AuxiliaryTooltipController __instance,
        Transform worldSpaceTransform,
        string newHeader
    ) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeAuxiliaryTooltipShowing(
            __instance,
            worldSpaceTransform,
            newHeader
        );
}

[HarmonyPatch(
    typeof(AuxiliaryTooltipController),
    nameof(AuxiliaryTooltipController.StartTooltipFadeOut)
)]
internal static class PostCombatImpactAuxiliaryTooltipHidePatch
{
    [HarmonyPrefix]
    private static void Prefix(AuxiliaryTooltipController __instance) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeAuxiliaryTooltipHiding(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ResetValues))]
internal static class PostCombatImpactTooltipResetPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeTooltipChanging(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ClearCurrentCard))]
internal static class PostCombatImpactTooltipClearPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeTooltipChanging(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), "OnDisable")]
internal static class PostCombatImpactTooltipDisablePatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeTooltipChanging(__instance);
}
