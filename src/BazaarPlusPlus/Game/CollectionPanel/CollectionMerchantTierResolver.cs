#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Event;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Spawning.SpawnBehaviors;
using BazaarGameShared.Domain.Spawning.SpawningContexts;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal readonly struct CollectionMerchantTierPolicy
{
    public CollectionMerchantTierPolicy(ETier? fixedTier, bool usesDayDistribution)
    {
        FixedTier = fixedTier;
        UsesDayDistribution = usesDayDistribution;
    }

    public ETier? FixedTier { get; }

    public bool UsesDayDistribution { get; }
}

internal static class CollectionMerchantTierResolver
{
    public static CollectionMerchantTierPolicy Resolve(TCardBase? template)
    {
        if (
            template is not TCardEncounterEvent eventTemplate
            || eventTemplate.SelectionContext?.SpawnContext is not TSpawnContextQuery spawnContext
        )
            return new CollectionMerchantTierPolicy(fixedTier: null, usesDayDistribution: true);

        ETier? fixedTier = null;
        var ignoresDayTable = false;
        if (spawnContext.Behaviors == null)
            return new CollectionMerchantTierPolicy(fixedTier: null, usesDayDistribution: true);

        foreach (var behavior in spawnContext.Behaviors)
        {
            if (behavior is TSpawnBehaviorIgnoreTierTable { IgnoreTierTable: true })
                ignoresDayTable = true;

            if (
                behavior is TSpawnBehaviorTier { IsNot: false } tierBehavior
                && tierBehavior.Tiers.Count == 1
            )
            {
                foreach (var tier in tierBehavior.Tiers)
                    fixedTier = tier;
            }
        }

        return fixedTier.HasValue
            ? new CollectionMerchantTierPolicy(fixedTier, usesDayDistribution: false)
            : new CollectionMerchantTierPolicy(
                fixedTier: null,
                usesDayDistribution: !ignoresDayTable
            );
    }
}
