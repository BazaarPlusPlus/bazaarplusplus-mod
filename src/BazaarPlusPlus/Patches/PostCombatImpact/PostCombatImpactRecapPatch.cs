#nullable enable
#pragma warning disable CS0436
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus.Patches.PostCombatImpact;

[HarmonyPatch(typeof(RecapItemVisualController), nameof(RecapItemVisualController.Initialize))]
internal static class PostCombatImpactRecapPatch
{
    // Initialize awaits card art before it finishes. Bind in a prefix so the recap card
    // owns the right-click target for its entire visible lifetime.
    [HarmonyPrefix]
    private static void Prefix(
        RecapItemVisualController __instance,
        Card cardData,
        CardController cardController
    )
    {
        if (cardData == null || cardController == null)
            return;

        BppPatchHost.Features.PostCombatImpact.BindRecapCard(__instance, cardData, cardController);
    }
}

[HarmonyPatch(typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerClick))]
internal static class PostCombatImpactSkillClickPatch
{
    private static readonly FieldInfo? TooltipOffsetField = AccessTools.Field(
        typeof(SkillProxyRenderer),
        "_tooltipOffsetWorldSpace"
    );

    [HarmonyPrefix]
    private static void Prefix(SkillProxyRenderer __instance, PointerEventData eventData)
    {
        var boardManager = Singleton<BoardManager>.Instance;
        var card = __instance.Card;
        if (
            eventData.button != PointerEventData.InputButton.Right
            || card == null
            || boardManager == null
            || !boardManager.IsRecapViewOpen
        )
            return;

        var offset = TooltipOffsetField?.GetValue(__instance) is Vector3 nativeOffset
            ? nativeOffset
            : Vector3.zero;
        var tooltipData = CardTooltipData.CreateCardTooltipData(card);
        if (tooltipData == null)
            return;

        BppPatchHost.Features.PostCombatImpact.ShowDetails(
            card,
            __instance.transform,
            offset,
            tooltipData
        );
    }
}

[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class PostCombatImpactTooltipRenderPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeTooltipChanging(__instance);
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
