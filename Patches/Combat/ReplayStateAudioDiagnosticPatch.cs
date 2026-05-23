#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(ReplayState), nameof(ReplayState.Replay))]
internal static class ReplayStateAudioDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        CombatReplayRuntime.LogReplayAudioState("ReplayState.Replay");
    }
}
