#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal static class CollectionDealerExplainResolver
{
    public static Dictionary<Guid, CollectionDealerCardExplain> Resolve(
        CollectionDealerSourceContext ctx,
        IReadOnlyList<CollectionCardVm> offeredCards
    )
    {
        var result = new Dictionary<Guid, CollectionDealerCardExplain>(offeredCards.Count);
        foreach (var card in offeredCards)
        {
            result[card.Id] = ExplainCard(ctx, card);
        }

        return result;
    }

    public static EstimateBucket? EstimateForCard(
        CollectionDealerSourceContext ctx,
        Guid targetId,
        IReadOnlyList<CollectionCardVm> offeredCards
    )
    {
        if (
            !ctx.EstimateEnabled
            || ctx.Kind != CollectionDealerSourceKind.Merchant
            || ctx.Hint is not { } hint
            || IsOutsideCardFilter(hint, targetId)
            || hint is not { Verified: true }
            || IsFixedCard(hint, targetId)
            || offeredCards.All(card => card.Id != targetId)
        )
        {
            return null;
        }

        var shop = new DealerShopDefinition
        {
            SourceKey = ctx.SourceKey,
            NumberCardsToSpawn = hint.NumberCardsToSpawn,
            CardIdFilters = hint.CardIdFilters,
            ItemTierFilters = hint.ItemTierFilters,
            RerollRepeats = hint.RerollRepeats,
            NativeItemTierProbability = ctx.NativeAssumption,
            TierWeights = DealerTierWeightReference.ForDay(ctx.Day),
        };
        var playerState = new DealerPlayerState { Day = ctx.Day };
        var pool = offeredCards
            .Select(card => new DealerCandidate
            {
                Id = card.Id,
                Type = card.Type,
                Size = card.Size,
                StartingTier = card.StartingTier,
            })
            .ToArray();

        var frequency = DealerProbabilityCore.AppearanceFrequency(
            targetId,
            shop,
            playerState,
            pool,
            trials: 4000,
            seed: 20260615
        );
        return ToBucket(frequency);
    }

    private static CollectionDealerCardExplain ExplainCard(
        CollectionDealerSourceContext ctx,
        CollectionCardVm card
    )
    {
        var eligibility = ResolveEligibility(ctx, card.StartingTier);
        var notes = ctx.SuppressDayGate ? new[] { "tier-specialist-loose" } : Array.Empty<string>();
        var state = ResolveState(ctx, card.Id);

        return new CollectionDealerCardExplain
        {
            State = state,
            InPool = state != CollectionDealerProbabilityState.NotInPool,
            NativeEligible = eligibility.NativeEligible,
            LooseEligible = eligibility.LooseEligible,
            DayGatePass = eligibility.DayGatePass,
            FixedDealVerified = state == CollectionDealerProbabilityState.Fixed ? true : null,
            Notes = notes,
        };
    }

    private static CollectionDealerProbabilityState ResolveState(
        CollectionDealerSourceContext ctx,
        Guid cardId
    )
    {
        if (!ctx.EstimateEnabled || ctx.Kind != CollectionDealerSourceKind.Merchant)
        {
            return CollectionDealerProbabilityState.Explain;
        }

        if (ctx.Hint is not { Verified: true } hint)
        {
            if (ctx.Hint is { } unverifiedHint && IsOutsideCardFilter(unverifiedHint, cardId))
            {
                return CollectionDealerProbabilityState.NotInPool;
            }

            return CollectionDealerProbabilityState.WeightsMissing;
        }

        if (IsOutsideCardFilter(hint, cardId))
        {
            return CollectionDealerProbabilityState.NotInPool;
        }

        if (IsFixedDirectDeal(hint))
        {
            return IsFixedCard(hint, cardId)
                ? CollectionDealerProbabilityState.Fixed
                : CollectionDealerProbabilityState.NotInPool;
        }

        return CollectionDealerProbabilityState.Estimate;
    }

    private static bool IsFixedDirectDeal(DealerShopHint hint) =>
        hint.CardIdFilters.Count > 0 && hint.NumberCardsToSpawn == hint.CardIdFilters.Count;

    private static bool IsFixedCard(DealerShopHint hint, Guid cardId) =>
        IsFixedDirectDeal(hint) && hint.CardIdFilters.Contains(cardId);

    private static bool IsOutsideCardFilter(DealerShopHint hint, Guid cardId) =>
        hint.CardIdFilters.Count > 0 && !hint.CardIdFilters.Contains(cardId);

    private static Eligibility ResolveEligibility(
        CollectionDealerSourceContext ctx,
        ETier startingTier
    )
    {
        if (UseReferenceEligibility(ctx))
        {
            return ResolveReferenceEligibility(ctx.Day, startingTier);
        }

        var dayGatePass =
            ctx.SuppressDayGate || DayTierSchedule.AllowsStartingTier(startingTier, ctx.Day);
        var looseEligible =
            ctx.SuppressDayGate && ctx.PinnedTier.HasValue
                ? CollectionCardFacetRanks.TierRank(startingTier)
                    <= CollectionCardFacetRanks.TierRank(ctx.PinnedTier.Value)
                : dayGatePass;
        var nativeEligible =
            !ctx.SuppressDayGate
            && dayGatePass
            && startingTier != ETier.Legendary
            && CollectionCardFacetRanks.TierRank(startingTier)
                <= CollectionCardFacetRanks.TierRank(DayTierSchedule.CeilingTier(ctx.Day));
        return new Eligibility(dayGatePass, looseEligible, nativeEligible);
    }

    private static bool UseReferenceEligibility(CollectionDealerSourceContext ctx) =>
        ctx.EstimateEnabled
        && ctx.Kind == CollectionDealerSourceKind.Merchant
        && ctx.Hint is { Verified: true };

    private static Eligibility ResolveReferenceEligibility(int day, ETier startingTier)
    {
        var weights = DealerTierWeightReference.ForDay(day);
        var weightedTiers = weights
            .Where(pair => pair.Value > 0)
            .Select(pair => pair.Key)
            .ToArray();
        if (weightedTiers.Length == 0)
        {
            return new Eligibility(false, false, false);
        }

        var nativeEligible =
            startingTier != ETier.Legendary && weightedTiers.Contains(startingTier);
        var maxLooseRank = weightedTiers.Max(TierRank);
        var looseEligible = LooseTierRank(startingTier) <= maxLooseRank;
        return new Eligibility(looseEligible, looseEligible, nativeEligible);
    }

    private static int TierRank(ETier tier) => CollectionCardFacetRanks.TierRank(tier);

    private static int LooseTierRank(ETier tier) =>
        TierRank(tier == ETier.Legendary ? ETier.Diamond : tier);

    private readonly record struct Eligibility(
        bool DayGatePass,
        bool LooseEligible,
        bool NativeEligible
    );

    private static EstimateBucket ToBucket(double frequency)
    {
        var tier =
            frequency >= 0.20 ? EstimateTier.High
            : frequency >= 0.07 ? EstimateTier.Medium
            : EstimateTier.Low;
        return EstimateBucket.Create(tier, Clamp01(frequency - 0.05), Clamp01(frequency + 0.05));
    }

    private static double Clamp01(double value)
    {
        if (value < 0)
        {
            return 0;
        }

        return value > 1 ? 1 : value;
    }
}
