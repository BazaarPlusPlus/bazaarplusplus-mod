#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal enum EstimateTier
{
    Low,
    Medium,
    High,
}

internal enum EstimateAuthority
{
    Reference,
}

internal sealed class EstimateBucket
{
    private EstimateBucket(EstimateTier tier, double low, double high)
    {
        Tier = tier;
        Low = low;
        High = high;
    }

    public EstimateTier Tier { get; }
    public double Low { get; }
    public double High { get; }
    public string Model { get; } = "old-bazaar-card-dealer";
    public EstimateAuthority Authority { get; } = EstimateAuthority.Reference;
    public string Source { get; } = DealerTierWeightReference.SourceLabel;
    public bool ReferenceOnly { get; } = true;

    public static EstimateBucket Create(EstimateTier tier, double low, double high) =>
        new(tier, low, high);
}
