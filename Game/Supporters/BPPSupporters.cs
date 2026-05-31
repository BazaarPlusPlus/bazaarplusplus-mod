#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporters
{
    public static BPPSupporterSample Sample()
    {
        return BPPSupporterSampler.Sample(
            BPPSupporterCatalog.GetCurrentEntries(),
            () => Random.value
        );
    }
}
