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
    private sealed class CacheEntry
    {
        public DateTime CachedAtUtc;
        public List<TooltipSegment> Segments = new List<TooltipSegment>();
    }

    private static readonly Dictionary<string, CacheEntry> _cache =
        new Dictionary<string, CacheEntry>();

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

    public static List<TooltipSegment> BuildPreviewSegments(Card card)
    {
        return BuildPreviewSegments(card as ItemCard, ModState.AvailableEnchantments);
    }

    public static List<TooltipSegment> BuildPreviewSegments(
        ItemCard itemCard,
        IEnumerable<EEnchantmentType> availableEnchantments
    )
    {
        var empty = new List<TooltipSegment>();
        if (!IsEligible(itemCard))
            return empty;

        // Defensive guard: even if caller forgets to gate by state, never render in combat.
        if (Data.IsInCombat)
            return empty;

        var enchantments = itemCard.GetEnchantments();
        if (enchantments == null || enchantments.Count == 0)
            return empty;

        var candidates = availableEnchantments
            ?.Distinct()
            .Where(enchantment => enchantments.ContainsKey(enchantment))
            .ToList();

        if (candidates == null || candidates.Count == 0)
            candidates = enchantments.Keys.Distinct().ToList();

        var cacheKey = BuildCacheKey(itemCard, candidates);
        if (TryGetFromCache(cacheKey, out var cached))
            return cached;

        var segments = new List<TooltipSegment>();

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

        SaveToCache(cacheKey, segments);
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
        var colorHex = GetEnchantmentColorHex(enchantmentType);
        foreach (var tooltip in enchantment.Localization.Tooltips)
        {
            var content = tooltip?.Content;
            if (content == null)
                continue;

            var renderedText = RenderTooltipText(itemCard, content, enchantmentType, enchantment);
            if (string.IsNullOrWhiteSpace(renderedText))
                continue;

            yield return new TooltipSegment(
                $"<size=75%>\u00A0\u00A0· <color=#{colorHex}>{enchantmentLabel}</color>: {renderedText}</size>",
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

    private static string GetEnchantmentColorHex(EEnchantmentType enchantmentType)
    {
        return enchantmentType switch
        {
            EEnchantmentType.Heavy => "CB9F6E",
            EEnchantmentType.Golden => "FFCD19",
            EEnchantmentType.Icy => "3FC8F7",
            EEnchantmentType.Turbo => "00ECC3",
            EEnchantmentType.Shielded => "F4CF20",
            EEnchantmentType.Restorative => "8EEA31",
            EEnchantmentType.Toxic => "0EBE4F",
            EEnchantmentType.Fiery => "FF9F45",
            EEnchantmentType.Shiny => "98A8FE",
            EEnchantmentType.Deadly => "F5503D",
            EEnchantmentType.Radiant => "98A8FE",
            EEnchantmentType.Obsidian => "9D4A6F",
            _ => "FFFFFF",
        };
    }

    private static string RenderTooltipText(
        ItemCard itemCard,
        TLocalizableText content,
        EEnchantmentType previewEnchantment,
        TEnchantment previewEnchantmentTemplate
    )
    {
        var localized = GetLocalizedText(content);
        if (string.IsNullOrWhiteSpace(localized))
            return string.Empty;

        var originalEnchantment = itemCard.Enchantment;
        var originalAttributes = new Dictionary<ECardAttributeType, int?>();
        try
        {
            // Temporarily switch to preview enchantment so placeholders resolve against preview values.
            itemCard.Enchantment = previewEnchantment;
            if (previewEnchantmentTemplate?.Attributes != null)
            {
                foreach (var attribute in previewEnchantmentTemplate.Attributes)
                {
                    if (itemCard.Attributes.TryGetValue(attribute.Key, out var oldValue))
                        originalAttributes[attribute.Key] = oldValue;
                    else
                        originalAttributes[attribute.Key] = null;

                    itemCard.Attributes[attribute.Key] = attribute.Value;
                }
            }

            var builder = TooltipBuilder.Create(
                new TooltipContext
                {
                    Instance = itemCard,
                    Template = itemCard.Template!,
                    ValueContext = new ValueContext(Data.Run, itemCard),
                },
                localized
            );
            return RenderTooltipBuilder(builder).TrimEnd();
        }
        catch
        {
            return localized;
        }
        finally
        {
            foreach (var attribute in originalAttributes)
            {
                if (attribute.Value.HasValue)
                    itemCard.Attributes[attribute.Key] = attribute.Value.Value;
                else
                    itemCard.Attributes.Remove(attribute.Key);
            }

            itemCard.Enchantment = originalEnchantment;
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

    private static string RenderTooltipBuilder(TooltipBuilder builder)
    {
        var rendered = new System.Text.StringBuilder();
        foreach (var component in builder.Components)
        {
            if (
                component is ITooltipToken token
                && token.ReferencedAttribute.HasValue
                && token.ReferencedAttribute.Value.RequiresConversionToSecondsForTooltips()
            )
            {
                var seconds = TooltipExtensions.MillisecondsToSeconds(
                    token.Resolve().GetValueOrDefault()
                );
                rendered.Append(
                    seconds.IsDecimal() ? seconds.GetDecimalValueString() : seconds.ToString()
                );
            }
            else
            {
                rendered.Append(component.Render());
            }
        }

        return rendered.ToString();
    }

    private static string BuildCacheKey(ItemCard itemCard, List<EEnchantmentType> candidates)
    {
        var instanceId = itemCard.InstanceId.ToString();
        var currentEnchant = itemCard.Enchantment?.ToString() ?? "None";
        var candidateKey = string.Join(",", candidates.OrderBy(x => x).Select(x => x.ToString()));
        return $"{instanceId}|{itemCard.Section}|{currentEnchant}|{candidateKey}";
    }

    private static bool TryGetFromCache(string key, out List<TooltipSegment> segments)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow - entry.CachedAtUtc <= CacheDuration)
                {
                    segments = new List<TooltipSegment>(entry.Segments);
                    return true;
                }

                _cache.Remove(key);
            }
        }

        segments = null;
        return false;
    }

    private static void SaveToCache(string key, List<TooltipSegment> segments)
    {
        lock (_cache)
        {
            _cache[key] = new CacheEntry
            {
                CachedAtUtc = DateTime.UtcNow,
                Segments = new List<TooltipSegment>(segments),
            };

            if (_cache.Count > 256)
                _cache.Clear();
        }
    }
}
