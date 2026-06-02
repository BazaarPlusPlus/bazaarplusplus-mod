#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Event;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Spawning.SpawnFilters;
using BazaarGameShared.Domain.Spawning.SpawnFilters.Constraints;
using BazaarGameShared.Domain.Spawning.SpawnGroups;
using BazaarGameShared.Domain.Spawning.SpawningContexts;

namespace BazaarPlusPlus.GameInterop.EncounterOffers;

internal static class EncounterOfferStaticPoolResolver
{
    public static EncounterOfferPoolResult ResolveOfferedTemplateIds(
        IReadOnlyList<Guid> sourceTemplateIds,
        IReadOnlyList<EHero> uiHeroFilters,
        IReadOnlyDictionary<Guid, ITCard> cardMap
    )
    {
        if (sourceTemplateIds.Count == 0)
            return EncounterOfferPoolResult.Unavailable("source-template-ids-empty");
        if (cardMap.Count == 0)
            return EncounterOfferPoolResult.Loading("static-card-map-empty");

        var offeredTemplateIds = new HashSet<Guid>();
        var sourceCardsResolved = 0;
        var sourceGroupsResolved = 0;

        foreach (var sourceTemplateId in sourceTemplateIds)
        {
            if (!cardMap.TryGetValue(sourceTemplateId, out var sourceCard))
                continue;
            if (sourceCard is not TCardEncounterEvent sourceEncounter)
                continue;

            sourceCardsResolved++;
            if (sourceEncounter.SelectionContext?.SpawnContext is not TSpawnContextQuery context)
                continue;

            foreach (var group in context.Groups)
            {
                sourceGroupsResolved++;
                foreach (var candidateId in ResolveGroup(group, cardMap, uiHeroFilters))
                    offeredTemplateIds.Add(candidateId);
            }
        }

        if (sourceCardsResolved == 0)
            return EncounterOfferPoolResult.Unavailable("static-source-cards-unavailable");
        if (sourceGroupsResolved == 0)
            return EncounterOfferPoolResult.Unavailable("static-source-groups-unavailable");

        return EncounterOfferPoolResult.Ready(offeredTemplateIds);
    }

    private static IEnumerable<Guid> ResolveGroup(
        TSpawnGroup group,
        IReadOnlyDictionary<Guid, ITCard> cardMap,
        IReadOnlyList<EHero> uiHeroFilters
    )
    {
        var filters = group.Filters ?? new List<ITSpawnFilter>();
        var hasIdFilter = HasIdFilter(filters);
        var applyUiHeroFilter = !HasSourceHeroConstraint(filters);
        var tierBehavior = ResolveTierBehavior(group);

        foreach (var pair in cardMap)
        {
            var candidate = pair.Value;
            if (candidate == null || candidate.Id == Guid.Empty)
                continue;
            if (!IsSpawnEligible(candidate, hasIdFilter))
                continue;
            if (applyUiHeroFilter && !MatchesUiHeroFilter(candidate, uiHeroFilters))
                continue;
            if (!MatchesTierBehavior(candidate, tierBehavior))
                continue;
            if (!MatchesFilters(candidate, filters))
                continue;

            yield return candidate.Id;
        }
    }

    private static bool HasIdFilter(IReadOnlyList<ITSpawnFilter> filters)
    {
        foreach (var filter in filters)
            if (filter is TSpawnFilterIdList)
                return true;
        return false;
    }

    private static bool HasSourceHeroConstraint(IReadOnlyList<ITSpawnFilter> filters)
    {
        foreach (var filter in filters)
        {
            if (filter is TSpawnFilterQuery query && HasHeroConstraint(query.Constraints))
                return true;
        }
        return false;
    }

    private static bool HasHeroConstraint(IConstraint constraint)
    {
        switch (constraint)
        {
            case ConstraintHero:
            case ConstraintIsOnlyHero:
                return true;

            case ConstraintAnd and:
                foreach (var child in and.Constraints)
                {
                    if (HasHeroConstraint(child))
                        return true;
                }
                return false;

            case ConstraintOr or:
                foreach (var child in or.Constraints)
                {
                    if (HasHeroConstraint(child))
                        return true;
                }
                return false;

            default:
                return false;
        }
    }

    private static IReadOnlyCollection<ETier>? ResolveTierBehavior(TSpawnGroup group)
    {
        if (group.Behaviors == null)
            return null;

        foreach (var behavior in group.Behaviors)
        {
            if (behavior is BazaarGameShared.Domain.Spawning.SpawnBehaviors.TSpawnBehaviorTier tier)
                return tier.Tiers;
        }

        return null;
    }

    private static bool MatchesFilters(ITCard candidate, IReadOnlyList<ITSpawnFilter> filters)
    {
        foreach (var filter in filters)
        {
            if (!MatchesFilter(candidate, filter))
                return false;
        }
        return true;
    }

    private static bool MatchesFilter(ITCard candidate, ITSpawnFilter filter) =>
        filter switch
        {
            TSpawnFilterIdList idList => idList.Ids.Contains(candidate.Id),
            TSpawnFilterQuery query => MatchesConstraint(candidate, query.Constraints),
            // Target and upgrade filters depend on current-board state, so they are not part
            // of the Collection Panel's static source-offer expansion.
            TSpawnFilterTarget => true,
            TSpawnFilterUpgrade => true,
            _ => true,
        };

    private static bool MatchesConstraint(ITCard candidate, IConstraint constraint)
    {
        switch (constraint)
        {
            case ConstraintAnd and:
                foreach (var child in and.Constraints)
                {
                    if (!MatchesConstraint(candidate, child))
                        return false;
                }
                return true;

            case ConstraintOr or:
                if (or.Constraints.Count == 0)
                    return false;
                foreach (var child in or.Constraints)
                {
                    if (MatchesConstraint(candidate, child))
                        return true;
                }
                return false;

            case ConstraintCardType cardType:
                return ApplyNegation(Overlaps(cardType.Types, candidate.Type), cardType.IsNot);

            case ConstraintHero hero:
                return ApplyNegation(Overlaps(candidate.Heroes, hero.Heroes), hero.IsNot);

            case ConstraintHiddenTag hiddenTag:
                return ApplyNegation(
                    Overlaps(candidate.HiddenTags, hiddenTag.HiddenTags),
                    hiddenTag.IsNot
                );

            case ConstraintId id:
                return ApplyNegation(id.Ids.Contains(candidate.Id), id.IsNot);

            case ConstraintIsOnlyHero onlyHero:
                return candidate.Heroes.Count == 1 && Overlaps(candidate.Heroes, onlyHero.Heroes);

            case ConstraintSize size:
                return ApplyNegation(Overlaps(size.Sizes, candidate.Size), size.IsNot);

            case ConstraintTag tag:
                return ApplyNegation(Overlaps(candidate.Tags, tag.Tags), tag.IsNot);

            case ConstraintTier tier:
                return ApplyNegation(Overlaps(tier.Tiers, candidate.StartingTier), tier.IsNot);

            case ConstraintEnchantmentEligible enchantment:
                return ApplyNegation(
                    IsEnchantmentEligible(candidate, enchantment),
                    enchantment.IsNot
                );

            default:
                return true;
        }
    }

    private static bool IsSpawnEligible(ITCard candidate, bool fromExplicitIdFilter) =>
        candidate.SpawningEligibility == ESpawnEligibility.Always
        || (fromExplicitIdFilter && candidate.SpawningEligibility == ESpawnEligibility.GuidOnly);

    private static bool MatchesUiHeroFilter(ITCard candidate, IReadOnlyList<EHero> uiHeroFilters)
    {
        if (uiHeroFilters.Count == 0)
            return true;
        return Overlaps(candidate.Heroes, uiHeroFilters);
    }

    private static bool MatchesTierBehavior(
        ITCard candidate,
        IReadOnlyCollection<ETier>? tierBehavior
    )
    {
        if (tierBehavior == null || tierBehavior.Count == 0)
            return true;

        foreach (var tier in tierBehavior)
        {
            if (IsCandidateTierEligible(candidate.StartingTier, tier))
                return true;
        }
        return false;
    }

    private static bool IsCandidateTierEligible(ETier candidateStartingTier, ETier sourceTier) =>
        TierRank(candidateStartingTier) <= TierRank(sourceTier);

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

    private static bool IsEnchantmentEligible(
        ITCard candidate,
        ConstraintEnchantmentEligible enchantment
    )
    {
        if (candidate is not TCardItem item || item.Enchantments == null)
            return false;
        if (enchantment.Enchantments.Count == 0)
            return item.Enchantments.Count > 0;

        foreach (var enchantmentType in enchantment.Enchantments)
        {
            if (item.Enchantments.ContainsKey(enchantmentType))
                return true;
        }
        return false;
    }

    private static bool ApplyNegation(bool value, bool isNot) => isNot ? !value : value;

    private static bool Overlaps<T>(IReadOnlyCollection<T> left, T right)
    {
        var comparer = EqualityComparer<T>.Default;
        foreach (var value in left)
        {
            if (comparer.Equals(value, right))
                return true;
        }
        return false;
    }

    private static bool Overlaps<T>(IReadOnlyCollection<T> left, IReadOnlyCollection<T> right)
    {
        var comparer = EqualityComparer<T>.Default;
        foreach (var value in left)
        {
            foreach (var candidate in right)
            {
                if (comparer.Equals(value, candidate))
                    return true;
            }
        }
        return false;
    }
}
