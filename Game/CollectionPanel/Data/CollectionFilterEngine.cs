#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Pure function: (catalog + filter state) -> ordered visible list.
//
// Ordering is deliberately explicit: selected priority first, then the other card facet, then
// display name.
// The rank helpers keep visible ordering independent from raw enum integer values.
internal static class CollectionFilterEngine
{
    public static List<CollectionCardVm> Apply(
        IReadOnlyList<CollectionCardVm> all,
        CollectionFilterState filter,
        CollectionFilterContext? context = null
    )
    {
        context ??= new CollectionFilterContext();
        var result = new List<CollectionCardVm>(all.Count);
        var offerPoolSet =
            context.OfferedCardIds == null
                ? null
                : context.OfferedCardIds as HashSet<Guid>
                    ?? new HashSet<Guid>(context.OfferedCardIds);
        var heroFilterCount = context.ApplyHeroFilter ? filter.Heroes.Count : 0;
        var tierFilterCount = filter.Tiers.Count;
        var tagFilterCount = filter.Tags.Count;
        var merchantFilterCount = filter.Merchants.Count;
        // Size only narrows Items; Skills are a single size, so skip it on the Skill tab.
        var sizeFilterCount = filter.ActiveType == ECardType.Item ? filter.Sizes.Count : 0;

        foreach (var card in all)
        {
            if (card.Type != filter.ActiveType)
                continue;
            if (offerPoolSet != null && !offerPoolSet.Contains(card.Id))
                continue;
            if (!filter.IncludePackages && card.IsPackage)
                continue;
            if (heroFilterCount > 0 && !CollectionHeroScope.MatchesFilter(card, filter))
                continue;
            if (tierFilterCount > 0 && !filter.Tiers.Contains(card.StartingTier))
                continue;
            if (tagFilterCount > 0 && !AnyTagMatch(card.Tags, filter.Tags))
                continue;
            if (sizeFilterCount > 0 && !filter.Sizes.Contains(card.Size))
                continue;
            if (merchantFilterCount > 0 && !AnyMerchantMatch(card.Merchants, filter.Merchants))
                continue;
            result.Add(card);
        }

        result.Sort(
            (a, b) =>
            {
                var facetOrder =
                    filter.SortPriority == CollectionSortPriority.Size
                        ? CompareBySizeThenTier(a, b)
                        : CompareByTierThenSize(a, b);
                if (facetOrder != 0)
                    return facetOrder;
                return string.Compare(
                    a.DisplayName,
                    b.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase
                );
            }
        );
        return result;
    }

    private static int CompareByTierThenSize(CollectionCardVm a, CollectionCardVm b)
    {
        var tierOrder = TierRank(a.StartingTier).CompareTo(TierRank(b.StartingTier));
        if (tierOrder != 0)
            return tierOrder;
        return SizeRank(a.Size).CompareTo(SizeRank(b.Size));
    }

    private static int CompareBySizeThenTier(CollectionCardVm a, CollectionCardVm b)
    {
        var sizeOrder = SizeRank(a.Size).CompareTo(SizeRank(b.Size));
        if (sizeOrder != 0)
            return sizeOrder;
        return TierRank(a.StartingTier).CompareTo(TierRank(b.StartingTier));
    }

    private static bool AnyTagMatch(
        IReadOnlyCollection<ECardTag> cardTags,
        HashSet<ECardTag> filterTags
    )
    {
        foreach (var tag in cardTags)
        {
            if (filterTags.Contains(tag))
                return true;
        }
        return false;
    }

    private static bool AnyMerchantMatch(
        IReadOnlyCollection<CollectionMerchantKind> cardMerchants,
        HashSet<CollectionMerchantKind> filterMerchants
    )
    {
        foreach (var merchant in cardMerchants)
        {
            if (filterMerchants.Contains(merchant))
                return true;
        }
        return false;
    }

    private static int TierRank(ETier tier) => CollectionCardFacetRanks.TierRank(tier);

    private static int SizeRank(ECardSize size) => CollectionCardFacetRanks.SizeRank(size);
}
