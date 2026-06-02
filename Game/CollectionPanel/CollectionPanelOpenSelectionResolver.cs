#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal static class CollectionPanelOpenSelectionResolver
{
    public static CollectionPanelSelectionState Resolve(
        bool isInGameRun,
        EHero? currentHero,
        Guid? currentEncounterTemplateId,
        IReadOnlyCollection<Guid>? choiceSelectionTemplateIds,
        IEnumerable<CollectionSourceEntry> entries
    )
    {
        if (!isInGameRun || !IsConcreteHero(currentHero))
            return CollectionPanelSelectionState.Default;

        var hero = currentHero!.Value;
        var source = ResolveSource(
            hero,
            currentEncounterTemplateId,
            choiceSelectionTemplateIds,
            entries
        );

        return source == null
            ? new CollectionPanelSelectionState(
                hero,
                CollectionPanelSelectionState.DefaultMerchantSourceKey,
                CollectionSourceKind.Merchant
            )
            : new CollectionPanelSelectionState(hero, source.SourceKey, source.Kind);
    }

    internal static bool IsConcreteHero(EHero? hero) => hero.HasValue && hero.Value != EHero.Common;

    private static CollectionSourceEntry? ResolveSource(
        EHero hero,
        Guid? currentEncounterTemplateId,
        IReadOnlyCollection<Guid>? choiceSelectionTemplateIds,
        IEnumerable<CollectionSourceEntry> entries
    )
    {
        if (currentEncounterTemplateId.HasValue)
        {
            var currentSource = FindSource(hero, currentEncounterTemplateId.Value, entries);
            if (currentSource != null)
                return currentSource;
        }

        if (choiceSelectionTemplateIds == null || choiceSelectionTemplateIds.Count == 0)
            return null;

        foreach (var templateId in choiceSelectionTemplateIds)
        {
            if (templateId == Guid.Empty)
                continue;

            var source = FindSource(hero, templateId, entries);
            if (source != null)
                return source;
        }

        return null;
    }

    private static CollectionSourceEntry? FindSource(
        EHero hero,
        Guid templateId,
        IEnumerable<CollectionSourceEntry> entries
    )
    {
        foreach (var entry in entries)
        {
            if (!entry.AppliesToHero(hero))
                continue;

            foreach (var candidate in entry.SourceTemplateIds)
                if (candidate == templateId)
                    return entry;
        }

        return null;
    }
}
