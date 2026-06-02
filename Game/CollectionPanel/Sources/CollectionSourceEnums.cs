#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal enum CollectionSourceKind
{
    Merchant,
    Trainer,
}

internal enum CollectionSourceHeroMode
{
    SelectedHero,
    AllHeroes,
    FixedHero,
    NeutralOnly,
}

internal enum CollectionSourceStartingTierMode
{
    AtMost,
    Exact,
}

internal enum CollectionSourceOfferPoolStatus
{
    Ready,
    NoneSelected,
}
