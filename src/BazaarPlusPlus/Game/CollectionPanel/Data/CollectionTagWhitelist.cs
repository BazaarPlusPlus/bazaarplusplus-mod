#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Player-facing ECardTag filter options, ordered to mirror BazaarDB's Types & Tags type slice.
// Event/combat/system-only tags stay out; catalog availability then removes options that current
// game data does not actually use.
internal static class CollectionTagWhitelist
{
    public static readonly IReadOnlyList<ECardTag> Ordered = PlayerFacingCardTags.Ordered;
}
