#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal static class DealerProbabilityCore
{
    public static IReadOnlyList<Guid> SimulateOneDeal(
        DealerShopDefinition shop,
        DealerPlayerState state,
        IReadOnlyList<DealerCandidate> pool,
        IRng rng
    )
    {
        var current = BuildFilteredPool(shop, state, pool);
        if (current.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        if (
            shop.CardIdFilters.Count > 0
            && shop.CardIdFilters.Count == shop.NumberCardsToSpawn
        )
        {
            return shop.CardIdFilters.ToArray();
        }

        current = ApplyRerollExclusion(shop, state, current);
        if (current.All(candidate => candidate.Type == ECardType.Skill))
        {
            return Array.Empty<Guid>();
        }

        if (current.Count < shop.NumberCardsToSpawn)
        {
            return Array.Empty<Guid>();
        }

        var dealt = new List<Guid>(shop.NumberCardsToSpawn);
        var nativeMissLatched = false;
        while (dealt.Count < shop.NumberCardsToSpawn)
        {
            if (current.Count == 0)
            {
                return Array.Empty<Guid>();
            }

            var useNative =
                shop.ItemTierFilters.Count == 0
                && !nativeMissLatched
                && rng.NextDouble() < shop.NativeItemTierProbability;
            var selectedTier = SelectRandomTier(shop.TierWeights, rng);

            DealerCandidate picked;
            if (useNative)
            {
                var nativeCandidates = current
                    .Where(candidate => candidate.StartingTier == selectedTier)
                    .ToArray();
                if (nativeCandidates.Length == 0)
                {
                    nativeMissLatched = true;
                    continue;
                }

                picked = nativeCandidates[rng.NextInt(nativeCandidates.Length)];
            }
            else
            {
                var looseTier =
                    shop.ItemTierFilters.Count > 0
                        ? shop.ItemTierFilters[rng.NextInt(shop.ItemTierFilters.Count)]
                        : selectedTier;
                var narrowed = current
                    .Where(candidate => TierRank(candidate.StartingTier) <= TierRank(looseTier))
                    .ToList();
                if (narrowed.Count > 0)
                {
                    current = narrowed;
                }

                picked = current[rng.NextInt(current.Count)];
            }

            dealt.Add(picked.Id);
            current.Remove(picked);
        }

        return dealt;
    }

    public static double AppearanceFrequency(
        Guid target,
        DealerShopDefinition shop,
        DealerPlayerState state,
        IReadOnlyList<DealerCandidate> pool,
        int trials,
        int seed
    )
    {
        if (trials <= 0)
        {
            return 0;
        }

        var rng = new SeededRng(seed);
        var appearances = 0;
        for (var i = 0; i < trials; i++)
        {
            if (SimulateOneDeal(shop, state, pool, rng).Contains(target))
            {
                appearances++;
            }
        }

        return appearances / (double)trials;
    }

    private static List<DealerCandidate> BuildFilteredPool(
        DealerShopDefinition shop,
        DealerPlayerState state,
        IReadOnlyList<DealerCandidate> pool
    )
    {
        IEnumerable<DealerCandidate> filtered = pool;
        if (shop.CardIdFilters.Count > 0)
        {
            var filterIds = shop.CardIdFilters.ToHashSet();
            filtered = filtered.Where(candidate => filterIds.Contains(candidate.Id));
        }

        if (state.PlayerSkillCardIds.Count > 0)
        {
            var skillIds = state.PlayerSkillCardIds.ToHashSet();
            filtered = filtered.Where(candidate => !skillIds.Contains(candidate.Id));
        }

        return filtered.ToList();
    }

    private static List<DealerCandidate> ApplyRerollExclusion(
        DealerShopDefinition shop,
        DealerPlayerState state,
        List<DealerCandidate> current
    )
    {
        if (!shop.RerollRepeats && state.RerollExclusionIds.Count > 0)
        {
            var rerollIds = state.RerollExclusionIds.ToHashSet();
            var rerollFiltered = current
                .Where(candidate => !rerollIds.Contains(candidate.Id))
                .ToList();
            if (rerollFiltered.Count >= shop.NumberCardsToSpawn)
            {
                current = rerollFiltered;
            }
        }

        return current;
    }

    private static ETier SelectRandomTier(IReadOnlyDictionary<ETier, double> weights, IRng rng)
    {
        var roll = rng.NextDouble();
        var cumulative = 0.0;
        foreach (
            var (tier, weight) in weights
                .OrderBy(pair => pair.Value)
                .ThenBy(pair => TierRank(pair.Key))
        )
        {
            cumulative += weight;
            if (roll <= cumulative)
            {
                return tier;
            }
        }

        return ETier.Bronze;
    }

    private static int TierRank(ETier tier) => CollectionCardFacetRanks.TierRank(tier);
}
