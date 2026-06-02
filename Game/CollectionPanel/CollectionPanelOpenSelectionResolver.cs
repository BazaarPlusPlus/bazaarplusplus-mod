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
        var sourceKey =
            ResolveMerchantSourceKey(
                hero,
                currentEncounterTemplateId,
                choiceSelectionTemplateIds,
                entries
            ) ?? CollectionPanelSelectionState.DefaultMerchantSourceKey;

        return new CollectionPanelSelectionState(hero, sourceKey);
    }

    internal static bool IsConcreteHero(EHero? hero) => hero.HasValue && hero.Value != EHero.Common;

    private static string? ResolveMerchantSourceKey(
        EHero hero,
        Guid? currentEncounterTemplateId,
        IReadOnlyCollection<Guid>? choiceSelectionTemplateIds,
        IEnumerable<CollectionSourceEntry> entries
    )
    {
        if (currentEncounterTemplateId.HasValue)
        {
            var currentSourceKey = FindMerchantSourceKey(
                hero,
                currentEncounterTemplateId.Value,
                entries
            );
            if (!string.IsNullOrWhiteSpace(currentSourceKey))
                return currentSourceKey;
        }

        if (choiceSelectionTemplateIds == null || choiceSelectionTemplateIds.Count == 0)
            return null;

        foreach (var templateId in choiceSelectionTemplateIds)
        {
            if (templateId == Guid.Empty)
                continue;

            var sourceKey = FindMerchantSourceKey(hero, templateId, entries);
            if (!string.IsNullOrWhiteSpace(sourceKey))
                return sourceKey;
        }

        return null;
    }

    private static string? FindMerchantSourceKey(
        EHero hero,
        Guid templateId,
        IEnumerable<CollectionSourceEntry> entries
    )
    {
        foreach (var entry in entries)
        {
            if (entry.Kind != CollectionSourceKind.Merchant)
                continue;
            if (!entry.AppliesToHero(hero))
                continue;

            foreach (var candidate in entry.SourceTemplateIds)
                if (candidate == templateId)
                    return entry.SourceKey;
        }

        return null;
    }
}
