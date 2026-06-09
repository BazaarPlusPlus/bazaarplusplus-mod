#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionFacetAvailability
{
    public static IReadOnlyList<ECardTag> TagsFor(
        IReadOnlyList<CollectionCardVm> cards,
        ECardType type
    )
    {
        if (cards.Count == 0)
            return Array.Empty<ECardTag>();

        var present = new HashSet<ECardTag>();
        foreach (var card in cards)
        {
            if (!IsFacetSource(card, type))
                continue;
            foreach (var tag in card.Tags)
                present.Add(tag);
        }

        var available = new List<ECardTag>(CollectionTagWhitelist.Ordered.Count);
        foreach (var tag in CollectionTagWhitelist.Ordered)
            if (present.Contains(tag))
                available.Add(tag);
        return available;
    }

    public static IReadOnlyList<EHiddenTag> KeywordsFor(
        IReadOnlyList<CollectionCardVm> cards,
        ECardType type
    )
    {
        if (cards.Count == 0)
            return Array.Empty<EHiddenTag>();

        var present = new HashSet<EHiddenTag>();
        foreach (var card in cards)
        {
            if (!IsFacetSource(card, type))
                continue;
            foreach (var keyword in card.HiddenTags)
                present.Add(keyword);
        }

        var available = new List<EHiddenTag>(CollectionKeywordWhitelist.Ordered.Count);
        foreach (var keyword in CollectionKeywordWhitelist.Ordered)
            if (present.Contains(keyword))
                available.Add(keyword);
        return available;
    }

    private static bool IsFacetSource(CollectionCardVm card, ECardType type) =>
        card.Type == type && !card.IsPackage;
}
