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
