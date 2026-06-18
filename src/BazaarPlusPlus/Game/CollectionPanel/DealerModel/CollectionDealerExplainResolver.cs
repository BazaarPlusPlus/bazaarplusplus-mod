#nullable enable
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

    private static CollectionDealerCardExplain ExplainCard(
        CollectionDealerSourceContext ctx,
        CollectionCardVm card
    )
    {
        var dayGatePass =
            ctx.SuppressDayGate || DayTierSchedule.AllowsStartingTier(card.StartingTier, ctx.Day);
        var looseEligible =
            ctx.SuppressDayGate && ctx.PinnedTier.HasValue
                ? CollectionCardFacetRanks.TierRank(card.StartingTier)
                    <= CollectionCardFacetRanks.TierRank(ctx.PinnedTier.Value)
                : dayGatePass;
        var nativeEligible =
            !ctx.SuppressDayGate
            && dayGatePass
            && card.StartingTier != ETier.Legendary
            && CollectionCardFacetRanks.TierRank(card.StartingTier)
                <= CollectionCardFacetRanks.TierRank(DayTierSchedule.CeilingTier(ctx.Day));
        var notes = ctx.SuppressDayGate
            ? new[] { "tier-specialist-loose" }
            : Array.Empty<string>();

        return new CollectionDealerCardExplain
        {
            State =
                ctx.EstimateEnabled && ctx.Hint == null
                    ? CollectionDealerProbabilityState.WeightsMissing
                    : CollectionDealerProbabilityState.Explain,
            InPool = true,
            NativeEligible = nativeEligible,
            LooseEligible = looseEligible,
            DayGatePass = dayGatePass,
            Notes = notes,
        };
    }
}
