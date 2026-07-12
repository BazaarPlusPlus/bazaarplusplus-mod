#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values.ReferenceValues;
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
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(CardTooltipController __instance, ref string text)
    {
        try
        {
            var content = string.IsNullOrEmpty(text) ? null : BuildContent(__instance);
            if (string.IsNullOrEmpty(content))
                return;

            text = AggregateItemMissingTypesText.AppendToPassiveText(text, content!);
        }
        catch (Exception ex)
        {
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
        if (template == null || !TryResolveTypeSource(template, out var source))
            return null;

        var present =
            source.Section == null
                ? card.Tags
                : ReadSectionTypes(source.Section.Value, source.ExcludeSelf ? card : null);
        return present == null
            ? null
            : AggregateItemMissingTypesText.Build(present, BppTooltipText.ColorKeywords);
    }

    internal static bool TryResolveTypeSource(TCardBase template, out TypeSource source)
    {
        foreach (var aura in template.Auras.Values)
        {
            if (aura.Action is TAuraActionCardAddTagsBySource)
            {
                source = TypeSource.LiveCard;
                return true;
            }
        }

        foreach (var ability in template.Abilities.Values)
        {
            if (ability.Action is TActionCardAddTagsBySource)
            {
                source = TypeSource.LiveCard;
                return true;
            }
        }

        // Forklift, Laurel's Fortress, Rowboat, and future equivalents aggregate
        // distinct types directly from an inventory section without copying those
        // tags onto themselves.
        foreach (var aura in template.Auras.Values)
        {
            if (
                aura.Action is TAuraActionCardModifyAttribute
                {
                    Value: TReferenceValueCardTagCount
                    {
                        Distinct: true,
                        Target: TTargetCardSection target
                    }
                }
            )
            {
                source = new TypeSource(target.TargetSection, target.ExcludeSelf);
                return true;
            }
        }

        source = default;
        return false;
    }

    private static HashSet<BazaarGameShared.Domain.Core.Types.ECardTag>? ReadSectionTypes(
        ETargetCardSectionTargetSection section,
        BazaarGameClient.Domain.Models.Cards.Card? excludedCard
    )
    {
        var player = TheBazaar.Data.Run?.Player;
        if (player == null)
            return null;

        var present = new HashSet<BazaarGameShared.Domain.Core.Types.ECardTag>();
        if (
            section
            is ETargetCardSectionTargetSection.SelfHand
                or ETargetCardSectionTargetSection.SelfHandAndStash
        )
            AddTags(present, player.Hand?.GetItemsAsEnumerable(), excludedCard);
        if (
            section
            is ETargetCardSectionTargetSection.SelfStash
                or ETargetCardSectionTargetSection.SelfHandAndStash
        )
            AddTags(present, player.Stash?.GetItemsAsEnumerable(), excludedCard);
        return present;
    }

    private static void AddTags(
        HashSet<BazaarGameShared.Domain.Core.Types.ECardTag> present,
        System.Collections.IEnumerable? cards,
        BazaarGameClient.Domain.Models.Cards.Card? excludedCard
    )
    {
        if (cards == null)
            return;

        foreach (var value in cards)
        {
            if (
                value is BazaarGameClient.Domain.Models.Cards.Card card
                && !ReferenceEquals(card, excludedCard)
            )
                present.UnionWith(card.Tags);
        }
    }

    internal readonly record struct TypeSource(
        ETargetCardSectionTargetSection? Section,
        bool ExcludeSelf
    )
    {
        public static TypeSource LiveCard => new(null, false);
    }
}
