#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal sealed class CollectionSourceEntry
{
    public CollectionSourceEntry(
        string sourceKey,
        CollectionSourceKind kind,
        string name,
        IReadOnlyList<EHero> availableHeroes,
        string description,
        Guid portraitTemplateId,
        IReadOnlyList<Guid> sourceTemplateIds,
        CollectionSourceOfferRule offerRule,
        string group,
        int order,
        int groupDisplayIndex
    )
    {
        SourceKey = sourceKey;
        Kind = kind;
        Name = name;
        AvailableHeroes = availableHeroes;
        Description = description;
        PortraitTemplateId = portraitTemplateId;
        SourceTemplateIds = sourceTemplateIds;
        OfferRule = offerRule;
        Group = group;
        Order = order;
        GroupDisplayIndex = groupDisplayIndex;
    }

    public string SourceKey { get; }

    public CollectionSourceKind Kind { get; }

    public string Name { get; }

    public IReadOnlyList<EHero> AvailableHeroes { get; }

    public string Description { get; }

    public Guid PortraitTemplateId { get; }

    public IReadOnlyList<Guid> SourceTemplateIds { get; }

    public CollectionSourceOfferRule OfferRule { get; }

    public string Group { get; }

    public int Order { get; }

    public int GroupDisplayIndex { get; }

    public bool AppliesToHero(EHero hero) =>
        AvailableHeroes.Count == 0 || AvailableHeroes.Contains(hero);
}
