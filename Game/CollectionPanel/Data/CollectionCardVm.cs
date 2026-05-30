#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Immutable projection of a card template into the fields the catalog/filters/virtualizer need.
// Single shared instance per Guid; filter engine and grid pass the same reference around.
internal sealed class CollectionCardVm
{
    public Guid Id { get; init; }
    public ECardType Type { get; init; }
    public ECardSize Size { get; init; }
    public ETier StartingTier { get; init; }
    public IReadOnlyCollection<EHero> Heroes { get; init; } = Array.Empty<EHero>();
    public IReadOnlyCollection<ECardTag> Tags { get; init; } = Array.Empty<ECardTag>();
    public string DisplayName { get; init; } = string.Empty;
    public string InternalName { get; init; } = string.Empty;
    public string ArtKey { get; init; } = string.Empty;

    public static CollectionCardVm From(TCardBase template)
    {
        return new CollectionCardVm
        {
            Id = template.Id,
            Type = template.Type,
            Size = template.Size,
            StartingTier = template.StartingTier,
            Heroes = template.Heroes,
            Tags = template.Tags,
            DisplayName =
                CollectionLocalizationResolver.ResolveTitle(template) ?? template.InternalName,
            InternalName = template.InternalName,
            ArtKey = template.ArtKey,
        };
    }
}
