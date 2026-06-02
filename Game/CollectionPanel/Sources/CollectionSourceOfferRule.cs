#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal sealed class CollectionSourceStartingTierRule
{
    public CollectionSourceStartingTierRule(CollectionSourceStartingTierMode mode, ETier tier)
    {
        Mode = mode;
        Tier = tier;
    }

    public CollectionSourceStartingTierMode Mode { get; }

    public ETier Tier { get; }
}

internal sealed class CollectionSourceOfferRule
{
    public CollectionSourceOfferRule(
        CollectionSourceHeroMode heroMode,
        EHero? hero,
        CollectionSourceStartingTierRule? startingTier,
        IReadOnlyList<ECardSize> sizesAny,
        IReadOnlyList<ECardTag> tagsAny,
        IReadOnlyList<ECardTag> tagsNone,
        IReadOnlyList<EHiddenTag> hiddenTagsAny,
        bool enchantableOnly
    )
    {
        HeroMode = heroMode;
        Hero = hero;
        StartingTier = startingTier;
        SizesAny = sizesAny ?? Array.Empty<ECardSize>();
        TagsAny = tagsAny ?? Array.Empty<ECardTag>();
        TagsNone = tagsNone ?? Array.Empty<ECardTag>();
        HiddenTagsAny = hiddenTagsAny ?? Array.Empty<EHiddenTag>();
        EnchantableOnly = enchantableOnly;
    }

    public CollectionSourceHeroMode HeroMode { get; }

    public EHero? Hero { get; }

    public CollectionSourceStartingTierRule? StartingTier { get; }

    public IReadOnlyList<ECardSize> SizesAny { get; }

    public IReadOnlyList<ECardTag> TagsAny { get; }

    public IReadOnlyList<ECardTag> TagsNone { get; }

    public IReadOnlyList<EHiddenTag> HiddenTagsAny { get; }

    public bool EnchantableOnly { get; }
}
