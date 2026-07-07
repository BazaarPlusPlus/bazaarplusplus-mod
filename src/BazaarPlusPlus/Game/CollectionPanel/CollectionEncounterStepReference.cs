#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionEncounterStepReference
{
    public CollectionEncounterStepReference(
        Guid templateId,
        IReadOnlyList<Guid>? prerequisiteTemplateIds = null,
        IReadOnlyList<IReadOnlyList<string>>? prerequisiteTagGroups = null
    )
    {
        TemplateId = templateId;
        PrerequisiteTemplateIds = Deduplicate(prerequisiteTemplateIds);
        PrerequisiteTagGroups = prerequisiteTagGroups ?? Array.Empty<IReadOnlyList<string>>();
    }

    public Guid TemplateId { get; }

    public IReadOnlyList<Guid> PrerequisiteTemplateIds { get; }

    // Tag-based ownership prerequisites ("if you have a Friend"): each inner list is an
    // any-of group; every group must be satisfied for the step to be offered.
    public IReadOnlyList<IReadOnlyList<string>> PrerequisiteTagGroups { get; }

    private static IReadOnlyList<Guid> Deduplicate(IReadOnlyList<Guid>? ids)
    {
        if (ids == null || ids.Count == 0)
            return Array.Empty<Guid>();

        var result = new List<Guid>(ids.Count);
        foreach (var id in ids)
        {
            if (id == Guid.Empty || result.Contains(id))
                continue;
            result.Add(id);
        }
        return result;
    }
}
