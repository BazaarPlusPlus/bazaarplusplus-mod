#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Immutable projection of a card template into the fields the catalog/filters/virtualizer need.
// Single shared instance per Guid; filter engine and grid pass the same reference around. The
// `From(TCardBase)` factory lives in CollectionCardVm.From.cs so this half stays free of the
// game card-model types and can be compiled into the CollectionGridLayout unit test.
internal sealed partial class CollectionCardVm
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
    public bool IsPackage { get; init; }
    public IReadOnlyCollection<CollectionMerchantKind> Merchants { get; init; } =
        Array.Empty<CollectionMerchantKind>();
}
