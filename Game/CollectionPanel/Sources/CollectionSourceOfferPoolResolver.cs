#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal static class CollectionSourceOfferPoolResolver
{
    public static CollectionSourceOfferPoolResult Resolve(
        CollectionSourceEntry? source,
        EHero? selectedHero,
        IReadOnlyList<CollectionCardVm> catalogCards
    )
    {
        if (catalogCards == null)
            throw new ArgumentNullException(nameof(catalogCards));
        if (source == null)
            return CollectionSourceOfferPoolResult.NoneSelected();

        var result = new HashSet<Guid>();
        foreach (var card in catalogCards)
        {
            if (Matches(source, selectedHero, card))
                result.Add(card.Id);
        }
        return CollectionSourceOfferPoolResult.Ready(result);
    }

    private static bool Matches(
        CollectionSourceEntry source,
        EHero? selectedHero,
        CollectionCardVm card
    )
    {
        if (card.Type != CardTypeFor(source.Kind))
            return false;
        var rule = source.OfferRule;
        if (!MatchesHero(rule, selectedHero, card.Heroes))
            return false;
        if (rule.StartingTier != null && !MatchesStartingTier(rule.StartingTier, card.StartingTier))
            return false;
        if (rule.SizesAny.Count > 0 && !Contains(rule.SizesAny, card.Size))
            return false;
        if (rule.TagsNone.Count > 0 && Overlaps(card.Tags, rule.TagsNone))
            return false;
        if (rule.TagsAny.Count > 0 && !Overlaps(card.Tags, rule.TagsAny))
            return false;
        if (rule.HiddenTagsAny.Count > 0 && !Overlaps(card.HiddenTags, rule.HiddenTagsAny))
            return false;
        if (rule.EnchantableOnly && !card.IsEnchantable)
            return false;
        return true;
    }

    private static ECardType CardTypeFor(CollectionSourceKind kind) =>
        kind == CollectionSourceKind.Trainer ? ECardType.Skill : ECardType.Item;

    private static bool MatchesHero(
        CollectionSourceOfferRule rule,
        EHero? selectedHero,
        IReadOnlyCollection<EHero> cardHeroes
    )
    {
        switch (rule.HeroMode)
        {
            case CollectionSourceHeroMode.AllHeroes:
                return true;

            case CollectionSourceHeroMode.FixedHero:
                return rule.Hero.HasValue && Contains(cardHeroes, rule.Hero.Value);

            case CollectionSourceHeroMode.NeutralOnly:
                return Contains(cardHeroes, EHero.Common);

            case CollectionSourceHeroMode.SelectedHero:
                if (!selectedHero.HasValue)
                    return true;
                return Contains(cardHeroes, selectedHero.Value);

            default:
                return false;
        }
    }

    private static bool MatchesStartingTier(
        CollectionSourceStartingTierRule rule,
        ETier candidateTier
    )
    {
        if (rule.Mode == CollectionSourceStartingTierMode.Exact)
            return candidateTier == rule.Tier;

        return CollectionCardFacetRanks.TierRank(candidateTier)
            <= CollectionCardFacetRanks.TierRank(rule.Tier);
    }

    private static bool Contains<T>(IReadOnlyCollection<T> values, T target)
    {
        var comparer = EqualityComparer<T>.Default;
        foreach (var value in values)
        {
            if (comparer.Equals(value, target))
                return true;
        }
        return false;
    }

    private static bool Overlaps<T>(IReadOnlyCollection<T> left, IReadOnlyCollection<T> right)
    {
        var comparer = EqualityComparer<T>.Default;
        foreach (var leftValue in left)
        {
            foreach (var rightValue in right)
            {
                if (comparer.Equals(leftValue, rightValue))
                    return true;
            }
        }
        return false;
    }
}
