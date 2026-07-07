#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionEncounterStepReference
{
    public CollectionEncounterStepReference(
        Guid templateId,
        IReadOnlyList<IReadOnlyList<Guid>>? prerequisiteIdGroups = null,
        IReadOnlyList<IReadOnlyList<string>>? prerequisiteTagGroups = null
    )
    {
        TemplateId = templateId;
        PrerequisiteIdGroups = prerequisiteIdGroups ?? Array.Empty<IReadOnlyList<Guid>>();
        PrerequisiteTagGroups = prerequisiteTagGroups ?? Array.Empty<IReadOnlyList<string>>();
    }

    public Guid TemplateId { get; }

    // Card-id ownership prerequisites: each inner list is an any-of group
    // ("Powder Keg or the Big One"); every group must be satisfied.
    public IReadOnlyList<IReadOnlyList<Guid>> PrerequisiteIdGroups { get; }

    // Tag-based ownership prerequisites ("if you have a Friend"): each inner list is an
    // any-of group; every group must be satisfied for the step to be offered.
    public IReadOnlyList<IReadOnlyList<string>> PrerequisiteTagGroups { get; }
}
