#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.GameInterop.CustomCards;

internal sealed record BppCustomCardDescriptor
{
    public Guid Id { get; init; }
    public ECardType Type { get; init; } = ECardType.Item;
    public ECardSize Size { get; init; } = ECardSize.Medium;
    public ETier StartingTier { get; init; } = ETier.Bronze;
    public LocalizedTextSet Title { get; init; } = new("", "", "");
    public LocalizedTextSet Description { get; init; } = new("", "", "");
    public bool HasBundledArt { get; init; }
    public string InternalName { get; init; } = string.Empty;
    public int SortKey { get; init; }
}
