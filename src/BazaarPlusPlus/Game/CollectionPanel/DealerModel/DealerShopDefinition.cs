#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class DealerShopDefinition
{
    public string SourceKey { get; init; } = string.Empty;
    public int NumberCardsToSpawn { get; init; } = 3;
    public IReadOnlyList<Guid> CardIdFilters { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<ETier> ItemTierFilters { get; init; } = Array.Empty<ETier>();
    public bool RerollRepeats { get; init; }
    public float NativeItemTierProbability { get; init; } = 0.8f;
    public IReadOnlyDictionary<ETier, double> TierWeights { get; init; } =
        new Dictionary<ETier, double>();
    public string Model { get; init; } = "old-bazaar-card-dealer";
}

internal sealed class DealerShopHint
{
    public int NumberCardsToSpawn { get; init; } = 3;
    public IReadOnlyList<Guid> CardIdFilters { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<ETier> ItemTierFilters { get; init; } = Array.Empty<ETier>();
    public bool RerollRepeats { get; init; }
    public bool Verified { get; init; }
}
