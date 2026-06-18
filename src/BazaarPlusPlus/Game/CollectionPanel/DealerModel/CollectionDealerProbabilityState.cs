#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal enum CollectionDealerProbabilityState
{
    NotInPool,
    Pool,
    Fixed,
    Explain,
    WeightsMissing,
    Estimate,
}
