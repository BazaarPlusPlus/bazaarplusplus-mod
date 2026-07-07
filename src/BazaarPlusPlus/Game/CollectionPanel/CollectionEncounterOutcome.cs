#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Raw spawn-group data for a random-outcome event (outer SelectionMethod=Random,
// Limit=1): the event rolls one group by weight instead of presenting choices.
internal sealed class CollectionEncounterOutcomeGroupData
{
    public CollectionEncounterOutcomeGroupData(
        uint weight,
        IReadOnlyList<Guid> ids,
        IReadOnlyList<CollectionEncounterOutcomeQueryPool> queryPools,
        IReadOnlyList<CollectionEncounterCardRequirement> requirements,
        CollectionEncounterDayCondition? dayCondition
    )
    {
        Weight = weight;
        Ids = ids;
        QueryPools = queryPools;
        Requirements = requirements;
        DayCondition = dayCondition;
    }

    public uint Weight { get; }

    public IReadOnlyList<Guid> Ids { get; }

    // Dynamic pools (TSpawnFilterQuery) spawned alongside/instead of fixed ids;
    // kept even when the constraints don't parse so the weight stays in the roll.
    public IReadOnlyList<CollectionEncounterOutcomeQueryPool> QueryPools { get; }

    // Card-count ownership prerequisites; all must be satisfied for the group to
    // participate in the roll.
    public IReadOnlyList<CollectionEncounterCardRequirement> Requirements { get; }

    public CollectionEncounterDayCondition? DayCondition { get; }
}

// A TSpawnFilterQuery pool inside an outcome group: the constraints summarized as a
// reward filter (null when unparseable) plus the group's spawn quantity.
internal sealed class CollectionEncounterOutcomeQueryPool
{
    public CollectionEncounterOutcomeQueryPool(
        CollectionEncounterRewardFilter? filter,
        int? quantity
    )
    {
        Filter = filter;
        Quantity = quantity;
    }

    public CollectionEncounterRewardFilter? Filter { get; }

    public int? Quantity { get; }
}

internal readonly struct CollectionEncounterDayCondition
{
    public CollectionEncounterDayCondition(int day, string comparison)
    {
        Day = day;
        Comparison = comparison;
    }

    public int Day { get; }

    public string Comparison { get; }

    public bool Matches(int currentDay) =>
        Comparison switch
        {
            "Equal" => currentDay == Day,
            "NotEqual" => currentDay != Day,
            "GreaterThan" => currentDay > Day,
            "GreaterThanOrEqual" => currentDay >= Day,
            "LessThan" => currentDay < Day,
            "LessThanOrEqual" => currentDay <= Day,
            _ => true,
        };
}

// Resolved outcome entry for display: one rolled alternative with its normalized
// percentage (null for prerequisite-unmet groups, which are outside the roll).
internal sealed class CollectionEncounterOutcomeView
{
    public CollectionEncounterOutcomeView(
        int? percent,
        bool isEligible,
        bool isCombatPool,
        int optionCount,
        IReadOnlyList<CollectionEncounterChoiceDetail> details
    )
    {
        Percent = percent;
        IsEligible = isEligible;
        IsCombatPool = isCombatPool;
        OptionCount = optionCount;
        Details = details;
    }

    public int? Percent { get; }

    public bool IsEligible { get; }

    public bool IsCombatPool { get; }

    public int OptionCount { get; }

    public IReadOnlyList<CollectionEncounterChoiceDetail> Details { get; }
}
