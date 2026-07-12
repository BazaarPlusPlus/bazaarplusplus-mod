#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class AggregateItemMissingTypesText
{
    private static readonly LocalizedTextSet Heading = new(
        "Missing Types:",
        "尚缺类型：",
        "尚缺類型："
    );

    // Player-facing item types currently present in game data. System-only tags
    // (Combat/Event/Unsellable/etc.) are deliberately excluded.
    internal static readonly IReadOnlyList<ECardTag> ItemTypes = new[]
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
        ECardTag.Property,
        ECardTag.Loot,
    };

    internal static IReadOnlyList<ECardTag> FindMissing(IEnumerable<ECardTag> present)
    {
        var presentSet = new HashSet<ECardTag>(present ?? Array.Empty<ECardTag>());
        return ItemTypes.Where(tag => !presentSet.Contains(tag)).ToArray();
    }

    internal static string? Build(
        IEnumerable<ECardTag> present,
        Func<string, string>? colorize = null
    )
    {
        var missing = FindMissing(present);
        if (missing.Count == 0)
            return null;

        var typeList = string.Join(", ", missing.Select(tag => tag.ToString()));
        var content = $"{L.Resolve(Heading)} {typeList}";
        return colorize?.Invoke(content) ?? content;
    }
}
