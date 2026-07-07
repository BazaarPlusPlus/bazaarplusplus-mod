#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionEncounterStepReference
{
    public CollectionEncounterStepReference(
        Guid templateId,
        IReadOnlyList<CollectionEncounterCardRequirement>? requirements = null
    )
    {
        TemplateId = templateId;
        Requirements = requirements ?? Array.Empty<CollectionEncounterCardRequirement>();
    }

    public Guid TemplateId { get; }

    // Card-count ownership prerequisites; all must be satisfied for the step to be
    // offered ("if you have Powder Keg or the Big One" is one requirement whose ids
    // are alternatives).
    public IReadOnlyList<CollectionEncounterCardRequirement> Requirements { get; }
}

// One spawn group of a choice event: fixed always-offered steps, or — when the
// group itself selects randomly — a pool the event rolls members from.
internal sealed class CollectionEncounterChoiceGroupData
{
    public CollectionEncounterChoiceGroupData(
        bool isRandomPool,
        IReadOnlyList<CollectionEncounterStepReference> members,
        CollectionEncounterDayCondition? dayCondition = null
    )
    {
        IsRandomPool = isRandomPool;
        Members = members;
        DayCondition = dayCondition;
    }

    public bool IsRandomPool { get; }

    public IReadOnlyList<CollectionEncounterStepReference> Members { get; }

    // Day-gated groups (Wishing Fountain carries one price tier per day); inactive
    // groups are not offered at all, so they must not render as choices.
    public CollectionEncounterDayCondition? DayCondition { get; }
}
