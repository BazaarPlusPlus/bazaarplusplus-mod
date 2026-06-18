#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class DealerCandidate
{
    public Guid Id { get; init; }
    public ECardType Type { get; init; }
    public ECardSize Size { get; init; }
    public ETier StartingTier { get; init; }
}
