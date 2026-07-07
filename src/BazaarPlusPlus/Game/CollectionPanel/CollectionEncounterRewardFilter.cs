#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionEncounterRewardFilter
{
    public CollectionEncounterRewardFilter(
        ECardType cardType,
        int? quantity,
        bool fromAnyHero,
        IReadOnlyList<ECardSize> sizes,
        IReadOnlyList<ETier> tiers,
        IReadOnlyList<ECardTag> tags,
        IReadOnlyList<EHiddenTag> keywords,
        string filterSummary,
        IReadOnlyList<ECardTag>? excludedTags = null,
        IReadOnlyList<EHiddenTag>? excludedKeywords = null
    )
    {
        CardType = cardType;
        Quantity = quantity;
        FromAnyHero = fromAnyHero;
        Sizes = sizes ?? Array.Empty<ECardSize>();
        Tiers = tiers ?? Array.Empty<ETier>();
        Tags = tags ?? Array.Empty<ECardTag>();
        Keywords = keywords ?? Array.Empty<EHiddenTag>();
        FilterSummary = filterSummary ?? string.Empty;
        ExcludedTags = excludedTags ?? Array.Empty<ECardTag>();
        ExcludedKeywords = excludedKeywords ?? Array.Empty<EHiddenTag>();
    }

    public ECardType CardType { get; }

    public int? Quantity { get; }

    public bool FromAnyHero { get; }

    public IReadOnlyList<ECardSize> Sizes { get; }

    public IReadOnlyList<ETier> Tiers { get; }

    public IReadOnlyList<ECardTag> Tags { get; }

    public IReadOnlyList<EHiddenTag> Keywords { get; }

    public string FilterSummary { get; }

    public IReadOnlyList<ECardTag> ExcludedTags { get; }

    public IReadOnlyList<EHiddenTag> ExcludedKeywords { get; }

    public bool HasTierGateOverride => Tiers.Count > 0;

    public CollectionEncounterRewardFilter WithFromAnyHero(bool fromAnyHero)
    {
        if (FromAnyHero == fromAnyHero)
            return this;

        return new CollectionEncounterRewardFilter(
            CardType,
            Quantity,
            fromAnyHero,
            Sizes,
            Tiers,
            Tags,
            Keywords,
            FilterSummary,
            ExcludedTags,
            ExcludedKeywords
        );
    }
}
