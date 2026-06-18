#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel.DealerModel;

internal sealed class CollectionDealerCardExplain
{
    public CollectionDealerProbabilityState State { get; init; }
    public bool InPool { get; init; }
    public bool NativeEligible { get; init; }
    public bool LooseEligible { get; init; }
    public bool DayGatePass { get; init; }
    public bool SkillBoundaryExcluded { get; init; }
    public bool? FixedDealVerified { get; init; }
    public EstimateBucket? Estimate { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
