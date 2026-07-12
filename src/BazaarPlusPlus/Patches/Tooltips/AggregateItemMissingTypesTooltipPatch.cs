#nullable enable
#pragma warning disable CS0436
using System;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class AggregateItemMissingTypesTooltipPatch
{
    private const string SectionKey = "aggregate-missing-types";

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CardTooltipController __instance, string text)
    {
        try
        {
            var content = string.IsNullOrEmpty(text) ? null : BuildContent(__instance);
            if (string.IsNullOrEmpty(content))
            {
                BppTooltipSections.Hide(__instance, SectionKey);
                return;
            }

            if (
                !BppTooltipSections.TryShow(
                    __instance,
                    SectionKey,
                    __instance.passiveEffectParent,
                    content!
                )
            )
                BppTooltipSections.Hide(__instance, SectionKey);
        }
        catch (Exception ex)
        {
            BppTooltipSections.Hide(__instance, SectionKey);
            BppLog.Error("AggregateTypesTooltip", "Failed to render missing item types", ex);
        }
    }

    private static string? BuildContent(CardTooltipController controller)
    {
        var card = controller._currentCard;
        if (card == null)
            return null;

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        var template = BppStaticDataAccess.GetCardTemplate(staticData, card.TemplateId);
        if (template == null || !AddsItemTypes(template))
            return null;

        // The live card's Tags are the source of truth: they already include both
        // the template's own types (for example Cargo Shorts is Apparel) and the
        // types copied by the native aura from the current hand/stash.
        return AggregateItemMissingTypesText.Build(card.Tags, BppTooltipText.ColorKeywords);
    }

    private static bool AddsItemTypes(TCardBase template)
    {
        foreach (var aura in template.Auras.Values)
        {
            if (aura.Action is TAuraActionCardAddTagsBySource)
                return true;
        }

        foreach (var ability in template.Abilities.Values)
        {
            if (ability.Action is TActionCardAddTagsBySource)
                return true;
        }

        return false;
    }
}
