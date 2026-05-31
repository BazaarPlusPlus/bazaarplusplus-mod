#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Stable collection-panel merchant buckets. The backing rule source can change later
// (manual IDs, spawner expansion, embedded JSON) without changing filter state shape.
internal enum CollectionMerchantKind
{
    General,
    Burn,
    Poison,
    Freeze,
    Slow,
    Haste,
    Speed,
    Toughness,
    Strength,
    Heal,
    Economy,
    Shield,
    Health,
    Joy,
    Flying,
}
