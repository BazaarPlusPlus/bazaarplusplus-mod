#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.Cards;

// Canonical ordering for card tags that are meaningful to players. Feature-specific
// consumers may filter this list further (for example aggregate item auras copy only
// item types, so Merchant is excluded there).
internal static class PlayerFacingCardTags
{
    public static readonly IReadOnlyList<ECardTag> Ordered = new[]
    {
        ECardTag.Weapon,
        ECardTag.Friend,
        ECardTag.Aquatic,
        ECardTag.Tool,
        ECardTag.Drone,
        ECardTag.Vehicle,
        ECardTag.Food,
        ECardTag.Trap,
        ECardTag.Toy,
        ECardTag.Potion,
        ECardTag.Reagent,
        ECardTag.Relic,
        ECardTag.Dragon,
        ECardTag.Core,
        ECardTag.Tech,
        ECardTag.Dinosaur,
        ECardTag.Ray,
        ECardTag.Apparel,
        ECardTag.Merchant,
        ECardTag.Property,
        ECardTag.Loot,
    };
}
