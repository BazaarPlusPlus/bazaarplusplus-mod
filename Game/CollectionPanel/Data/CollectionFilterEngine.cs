#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Pure function: (catalog + filter state) -> ordered visible list.
//
// Ordering is deliberately explicit (tier rank then display name) because ETier's underlying
// integer values place Diamond=3 before Legendary=4, which would mis-order a naive sort on
// the raw enum. TierRank() locks the intended ordering.
internal static class CollectionFilterEngine
{
    public static List<CollectionCardVm> Apply(
        IReadOnlyList<CollectionCardVm> all,
        CollectionFilterState filter
    )
    {
        var result = new List<CollectionCardVm>(all.Count);
        var search = filter.Search?.Trim() ?? string.Empty;
        var hasSearch = search.Length > 0;
        var heroFilterCount = filter.Heroes.Count;
        var tierFilterCount = filter.Tiers.Count;
        var merchantFilterCount = filter.Merchants.Count;
        // Size only narrows Items; Skills are a single size, so skip it on the Skill tab.
        var sizeFilterCount = filter.ActiveType == ECardType.Item ? filter.Sizes.Count : 0;

        foreach (var card in all)
        {
            if (card.Type != filter.ActiveType)
                continue;
            if (!filter.IncludePackages && card.IsPackage)
                continue;
            if (heroFilterCount > 0 && !AnyHeroMatch(card.Heroes, filter.Heroes))
                continue;
            if (tierFilterCount > 0 && !filter.Tiers.Contains(card.StartingTier))
                continue;
            if (sizeFilterCount > 0 && !filter.Sizes.Contains(card.Size))
                continue;
            if (merchantFilterCount > 0 && !AnyMerchantMatch(card.Merchants, filter.Merchants))
                continue;
            if (
                hasSearch
                && card.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                && card.InternalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
            )
                continue;
            result.Add(card);
        }

        result.Sort(
            (a, b) =>
            {
                var tierOrder = TierRank(a.StartingTier).CompareTo(TierRank(b.StartingTier));
                if (tierOrder != 0)
                    return tierOrder;
                return string.Compare(
                    a.DisplayName,
                    b.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase
                );
            }
        );
        return result;
    }

    private static bool AnyHeroMatch(
        IReadOnlyCollection<EHero> cardHeroes,
        HashSet<EHero> filterHeroes
    )
    {
        foreach (var hero in cardHeroes)
        {
            if (filterHeroes.Contains(hero))
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

    private static int TierRank(ETier tier) =>
        tier switch
        {
            ETier.Bronze => 0,
            ETier.Silver => 1,
            ETier.Gold => 2,
            ETier.Diamond => 3,
            ETier.Legendary => 4,
            _ => 99,
        };
}
