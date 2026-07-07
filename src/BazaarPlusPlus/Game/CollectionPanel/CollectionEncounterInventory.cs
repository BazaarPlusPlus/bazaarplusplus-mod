#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Snapshot of the player's current cards used to evaluate encounter-choice ownership
// prerequisites: specific-card checks by template id, "if you have a <Tag>" checks by
// the union of the owned cards' tag names (ECardTag + EHiddenTag, compared by name).
internal sealed class CollectionEncounterInventory
{
    private readonly HashSet<Guid> _templateIds;
    private readonly HashSet<string> _tagNames;

    public CollectionEncounterInventory(HashSet<Guid> templateIds, HashSet<string> tagNames)
    {
        _templateIds = templateIds;
        _tagNames = tagNames;
    }

    public bool OwnsTemplate(Guid templateId) => _templateIds.Contains(templateId);

    public bool OwnsAnyTemplate(IReadOnlyList<Guid> anyOfIds)
    {
        foreach (var id in anyOfIds)
            if (_templateIds.Contains(id))
                return true;
        return false;
    }

    public bool OwnsAnyTag(IReadOnlyList<string> anyOfTags)
    {
        foreach (var tag in anyOfTags)
            if (_tagNames.Contains(tag))
                return true;
        return false;
    }
}
