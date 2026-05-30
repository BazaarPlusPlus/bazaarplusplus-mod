#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Mutable selection state held by CollectionPanel; pure data. The filter engine reads
// this and produces an ordered visible set.
internal sealed class CollectionFilterState
{
    public ECardType ActiveType { get; set; } = ECardType.Item;
    public HashSet<EHero> Heroes { get; } = new();
    public HashSet<ETier> Tiers { get; } = new();
    public string Search { get; set; } = string.Empty;

    public void Reset()
    {
        ActiveType = ECardType.Item;
        Heroes.Clear();
        Tiers.Clear();
        Search = string.Empty;
    }
}
