#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal enum CollectionTabKind
{
    Items,
    Packages,
    Skills,
    Achievements,
}

internal static class CollectionTabKindExtensions
{
    public static ECardType CardType(this CollectionTabKind tab) =>
        tab == CollectionTabKind.Skills ? ECardType.Skill : ECardType.Item;

    public static bool IsPackageOnly(this CollectionTabKind tab) =>
        tab == CollectionTabKind.Packages;
}
