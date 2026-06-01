#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal enum CollectionSortPriority
{
    Quality,
    Size,
}

// Mutable selection state held by CollectionPanel; pure data. The filter engine reads
// this and produces an ordered visible set.
internal sealed class CollectionFilterState
{
    public ECardType ActiveType { get; set; } = ECardType.Item;
    public HashSet<EHero> Heroes { get; } = new();
    public HashSet<ETier> Tiers { get; } = new();
    public HashSet<ECardTag> Tags { get; } = new();

    // Item card size (Small/Medium/Large). Only meaningful on the Item tab — Skills are a single
    // size, so the engine ignores this set when ActiveType is Skill and the UI hides the row.
    public HashSet<ECardSize> Sizes { get; } = new();
    public HashSet<CollectionMerchantKind> Merchants { get; } = new();
    public bool IncludePackages { get; set; }
    public CollectionSortPriority SortPriority { get; set; } = CollectionSortPriority.Quality;
    public string Search { get; set; } = string.Empty;

    public bool HasActiveFilters =>
        Heroes.Count > 0
        || Tiers.Count > 0
        || Tags.Count > 0
        || Sizes.Count > 0
        || Merchants.Count > 0
        || IncludePackages
        || SortPriority != CollectionSortPriority.Quality
        || !string.IsNullOrWhiteSpace(Search);

    public void Reset()
    {
        ActiveType = ECardType.Item;
        Heroes.Clear();
        Tiers.Clear();
        Tags.Clear();
        Sizes.Clear();
        Merchants.Clear();
        IncludePackages = false;
        SortPriority = CollectionSortPriority.Quality;
        Search = string.Empty;
    }
}
