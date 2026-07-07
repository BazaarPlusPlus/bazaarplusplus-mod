#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionEncounterStepReference
{
    public CollectionEncounterStepReference(
        Guid templateId,
        IReadOnlyList<Guid>? prerequisiteTemplateIds = null
    )
    {
        TemplateId = templateId;
        PrerequisiteTemplateIds = Deduplicate(prerequisiteTemplateIds);
    }

    public Guid TemplateId { get; }

    public IReadOnlyList<Guid> PrerequisiteTemplateIds { get; }

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
