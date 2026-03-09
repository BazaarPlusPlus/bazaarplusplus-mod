using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Tooltips;
using BazaarGameShared.Domain.Values;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.Utilities;

namespace BazaarPlusPlus;

public static class ItemEnchantPreviewBuilder
{
    public static List<TooltipSegment> BuildPreviewSegments(Card card)
    {
        return BuildPreviewSegments(card as ItemCard, ModState.AvailableEnchantments);
    }

    public static List<TooltipSegment> BuildPreviewSegments(
        ItemCard itemCard,
        IEnumerable<EEnchantmentType> availableEnchantments
    )
    {
        var segments = new List<TooltipSegment>();
        if (!IsEligible(itemCard))
            return segments;

        var candidates = availableEnchantments?.Distinct().ToList();
        if (candidates == null || candidates.Count == 0)
            return segments;

        var enchantments = itemCard.GetEnchantments();
        if (enchantments == null || enchantments.Count == 0)
            return segments;

        foreach (var enchantmentType in candidates)
        {
            if (itemCard.Enchantment == enchantmentType)
                continue;

            if (!enchantments.TryGetValue(enchantmentType, out var enchantment))
                continue;

            foreach (
                var segment in BuildSegmentsForEnchantment(itemCard, enchantmentType, enchantment)
            )
                segments.Add(segment);
        }

        return segments;
    }

    private static bool IsEligible(ItemCard itemCard)
    {
        if (itemCard == null || itemCard.Type != ECardType.Item)
            return false;

        return itemCard.Section == EInventorySection.Hand
            || itemCard.Section == EInventorySection.Stash;
    }

    private static IEnumerable<TooltipSegment> BuildSegmentsForEnchantment(
        ItemCard itemCard,
        EEnchantmentType enchantmentType,
        TEnchantment enchantment
    )
    {
        if (
            enchantment.Localization?.Tooltips == null
            || enchantment.Localization.Tooltips.Count == 0
        )
            yield break;

        var enchantmentLabel = GetEnchantmentLabel(enchantmentType);
        foreach (var tooltip in enchantment.Localization.Tooltips)
        {
            var content = tooltip?.Content;
            if (content == null)
                continue;

            var renderedText = RenderTooltipText(itemCard, content);
            if (string.IsNullOrWhiteSpace(renderedText))
                continue;

            yield return new TooltipSegment(
                $"If {enchantmentLabel}: {renderedText}",
                null,
                null,
                -1
            );
        }
    }

    private static string GetEnchantmentLabel(EEnchantmentType enchantmentType)
    {
        try
        {
            return new LocalizableText(enchantmentType.ToString()).GetLocalizedText();
        }
        catch
        {
            return enchantmentType.ToString();
        }
    }

    private static string RenderTooltipText(ItemCard itemCard, TLocalizableText content)
    {
        var localized = GetLocalizedText(content);
        if (string.IsNullOrWhiteSpace(localized))
            return string.Empty;

        try
        {
            var builder = TooltipBuilder.Create(
                new TooltipContext
                {
                    Instance = itemCard,
                    Template = itemCard.Template!,
                    ValueContext = new ValueContext(Data.Run, itemCard),
                },
                localized
            );
            return builder.Render(canFuse: false).RemoveExtraSpaces();
        }
        catch
        {
            return localized;
        }
    }

    private static string GetLocalizedText(TLocalizableText content)
    {
        try
        {
            return content.GetLocalizedText();
        }
        catch
        {
            return content.Text ?? string.Empty;
        }
    }
}
