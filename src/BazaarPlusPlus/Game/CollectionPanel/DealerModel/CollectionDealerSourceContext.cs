#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal enum CollectionDealerSourceKind
{
    Merchant,
    Trainer,
}

internal sealed class CollectionDealerSourceContext
{
    public string SourceKey { get; init; } = string.Empty;
    public CollectionDealerSourceKind Kind { get; init; }
    public EHero? Hero { get; init; }
    public int Day { get; init; }
    public bool SuppressDayGate { get; init; }
    public ETier? PinnedTier { get; init; }
    public bool EstimateEnabled { get; init; }
    public float NativeAssumption { get; init; } = 0.8f;
    public object? Hint { get; init; }
}
