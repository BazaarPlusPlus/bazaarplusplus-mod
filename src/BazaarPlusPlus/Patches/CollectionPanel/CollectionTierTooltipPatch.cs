#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.CollectionPanel;

[HarmonyPatch(typeof(CardTooltipData), nameof(CardTooltipData.GetActiveAbilityTooltipBlock))]
internal static class CollectionTierActiveTooltipPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CardTooltipData __instance, ref List<TooltipSegment> __result)
    {
        if (CollectionTierTooltipPreview.IsRenderingVariant)
            return;

        try
        {
            CollectionTierTooltipPreview.MergeActive(__instance, __result);
        }
        catch (Exception ex)
        {
            BppLog.Error("CollectionTierTooltip", "Failed to merge active tier values.", ex);
        }
    }
}

[HarmonyPatch(typeof(CardTooltipData), nameof(CardTooltipData.GetPassiveTooltipBlock))]
internal static class CollectionTierPassiveTooltipPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(
        CardTooltipData __instance,
        ref ValueTuple<StringBuilder, TooltipSegment?> __result
    )
    {
        if (CollectionTierTooltipPreview.IsRenderingVariant)
            return;

        try
        {
            CollectionTierTooltipPreview.MergePassive(__instance, ref __result);
        }
        catch (Exception ex)
        {
            BppLog.Error("CollectionTierTooltip", "Failed to merge passive tier values.", ex);
        }
    }
}

[HarmonyPatch(typeof(CooldownRenderer), nameof(CooldownRenderer.RenderFromTooltip))]
internal static class CollectionTierCooldownTooltipPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CooldownRenderer __instance, CardTooltipData tooltipData)
    {
        try
        {
            if (
                !CollectionTierTooltipPreview.TryGetTierAttributeValues(
                    tooltipData,
                    BazaarGameShared.Domain.Core.Types.ECardAttributeType.CooldownMax,
                    "0.#",
                    out var values
                )
            )
                return;

            if (values.Count == 2)
            {
                __instance.SetCooldown(values[0].Text, canFuse: true, values[1].Text);
                return;
            }

            __instance.SetCooldown(CollectionTierTooltipTextMerger.MergeCooldown(values));
        }
        catch (Exception ex)
        {
            BppLog.Error("CollectionTierTooltip", "Failed to merge cooldown tier values.", ex);
        }
    }
}
