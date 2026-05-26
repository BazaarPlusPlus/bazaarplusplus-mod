#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using HarmonyLib;
using TheBazaar.Game.CardFrames;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch(typeof(PriceTagContainer), "PlayPriceChangeVFX")]
internal static class CombatReplayPriceTagVfxPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        var runtime = CombatReplayRuntime.Instance;
        return runtime?.IsReplayStartInProgress != true
            && runtime?.IsSavedReplayPlaybackActive != true;
    }
}
