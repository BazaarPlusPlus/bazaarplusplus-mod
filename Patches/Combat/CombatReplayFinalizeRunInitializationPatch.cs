#nullable enable
using System.Threading.Tasks;
using BazaarPlusPlus.Game.CombatReplay;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(StartRunAppState), nameof(StartRunAppState.FinalizeRunInitialization))]
internal static class CombatReplayFinalizeRunInitializationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Task __result)
    {
        var runtime = CombatReplayRuntime.Instance;
        if (
            runtime?.IsReplayStartInProgress != true
            && runtime?.IsSavedReplayPlaybackActive != true
        )
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }
}
