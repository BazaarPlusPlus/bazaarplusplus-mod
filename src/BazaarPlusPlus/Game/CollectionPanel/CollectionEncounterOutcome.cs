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
        IReadOnlyList<IReadOnlyList<Guid>> prerequisiteIdGroups,
        IReadOnlyList<IReadOnlyList<string>> prerequisiteTagGroups,
        CollectionEncounterDayCondition? dayCondition
    )
    {
        Weight = weight;
        Ids = ids;
        PrerequisiteIdGroups = prerequisiteIdGroups;
        PrerequisiteTagGroups = prerequisiteTagGroups;
        DayCondition = dayCondition;
    }

    public uint Weight { get; }

    public IReadOnlyList<Guid> Ids { get; }

    // Any-of card-id groups (one per prerequisite, e.g. "Powder Keg or the Big One").
    public IReadOnlyList<IReadOnlyList<Guid>> PrerequisiteIdGroups { get; }

    public IReadOnlyList<IReadOnlyList<string>> PrerequisiteTagGroups { get; }

    public CollectionEncounterDayCondition? DayCondition { get; }
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
