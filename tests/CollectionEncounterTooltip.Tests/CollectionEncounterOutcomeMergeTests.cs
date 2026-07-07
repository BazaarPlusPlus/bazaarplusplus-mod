using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.CollectionPanel;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public class CollectionEncounterOutcomeMergeTests
{
    private static readonly Guid MonsterA = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid MonsterB = Guid.Parse("a0000000-0000-0000-0000-000000000002");

    [Fact]
    public void Combat_pools_with_the_same_eligibility_merge_into_one_view()
    {
        // Mountain Pass shape: 25% fight + 25% fight + 40% loot + 10% skill; the
        // two combat groups carry no visible monster names, so they collapse to 50%.
        var views = CollectionEncounterEventDetailResolver.BuildOutcomeViews(
            new List<CollectionEncounterEventDetailResolver.OutcomeGroupResolution>
            {
                Combat(weight: 25, ids: new[] { MonsterA }),
                Combat(weight: 25, ids: new[] { MonsterB }),
                Rewards(weight: 40),
                Rewards(weight: 10),
            },
            totalWeight: 100
        );

        var combat = Assert.Single(views, view => view.IsCombatPool);
        Assert.Equal(50, combat.Percent);
        Assert.Equal(2, combat.OptionCount);
        Assert.Equal(3, views.Count);
        // Merged combat keeps the first combat group's position.
        Assert.True(views[0].IsCombatPool);
    }

    [Fact]
    public void Merged_combat_percent_rounds_the_summed_weight_not_the_parts()
    {
        var views = CollectionEncounterEventDetailResolver.BuildOutcomeViews(
            new List<CollectionEncounterEventDetailResolver.OutcomeGroupResolution>
            {
                Combat(weight: 33, ids: new[] { MonsterA }),
                Combat(weight: 33, ids: new[] { MonsterB }),
                Rewards(weight: 33),
            },
            totalWeight: 99
        );

        // 66/99 = 66.67% -> 67, not round(33.3) + round(33.3) = 66.
        Assert.Equal(67, views.Single(view => view.IsCombatPool).Percent);
    }

    [Fact]
    public void Overlapping_monster_pools_do_not_inflate_the_option_count()
    {
        var views = CollectionEncounterEventDetailResolver.BuildOutcomeViews(
            new List<CollectionEncounterEventDetailResolver.OutcomeGroupResolution>
            {
                Combat(weight: 50, ids: new[] { MonsterA, MonsterB }),
                Combat(weight: 50, ids: new[] { MonsterB }),
            },
            totalWeight: 100
        );

        var combat = Assert.Single(views);
        Assert.Equal(2, combat.OptionCount);
        Assert.Equal(100, combat.Percent);
    }

    [Fact]
    public void Ineligible_combat_pools_stay_separate_from_eligible_ones()
    {
        var views = CollectionEncounterEventDetailResolver.BuildOutcomeViews(
            new List<CollectionEncounterEventDetailResolver.OutcomeGroupResolution>
            {
                Combat(weight: 60, ids: new[] { MonsterA }),
                Combat(weight: 40, ids: new[] { MonsterB }, eligible: false),
            },
            totalWeight: 60
        );

        Assert.Equal(2, views.Count);
        Assert.Equal(100, views[0].Percent);
        Assert.False(views[1].IsEligible);
        Assert.Null(views[1].Percent);
    }

    private static CollectionEncounterEventDetailResolver.OutcomeGroupResolution Combat(
        uint weight,
        Guid[] ids,
        bool eligible = true
    ) =>
        new(
            weight,
            eligible,
            isCombatPool: true,
            new HashSet<Guid>(ids),
            new List<CollectionEncounterChoiceDetail>()
        );

    private static CollectionEncounterEventDetailResolver.OutcomeGroupResolution Rewards(
        uint weight
    ) =>
        new(
            weight,
            eligible: true,
            isCombatPool: false,
            new HashSet<Guid>(),
            new List<CollectionEncounterChoiceDetail>
            {
                new(
                    Guid.NewGuid(),
                    displayName: "Reward",
                    resultText: "Get a thing",
                    rewardFilter: null,
                    isSourceMatch: false
                ),
            }
        );
}
