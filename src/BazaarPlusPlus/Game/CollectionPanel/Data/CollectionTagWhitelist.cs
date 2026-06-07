#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Player-facing ECardTag filter options, ordered for display. Mirrors the in-game item
// vocabulary (Weapon..Core plus Ray..Instrument) and excludes mechanism tags
// (Unsellable/Unstashable/Merchant/Event/Combat/Loot) that never describe a browsable card.
// The first PrimaryCount entries are the chips shown while the tag row is collapsed; the
// rest sit behind the row's expand toggle.
internal static class CollectionTagWhitelist
{
    public const int PrimaryCount = 8;

    public static readonly IReadOnlyList<ECardTag> Ordered = new[]
    {
        // Primary (visible while collapsed).
        ECardTag.Weapon,
        ECardTag.Tool,
        ECardTag.Food,
        ECardTag.Potion,
        ECardTag.Friend,
        ECardTag.Property,
        ECardTag.Tech,
        ECardTag.Vehicle,
        // Extended (behind the expand toggle), alphabetical.
        ECardTag.Apparel,
        ECardTag.Aquatic,
        ECardTag.Core,
        ECardTag.Dinosaur,
        ECardTag.Dragon,
        ECardTag.Drone,
        ECardTag.Ingredient,
        ECardTag.Instrument,
        ECardTag.Key,
        ECardTag.Map,
        ECardTag.Ray,
        ECardTag.Reagent,
        ECardTag.Relic,
        ECardTag.Sigil,
        ECardTag.Toy,
        ECardTag.Trap,
    };
}
