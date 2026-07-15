#nullable enable
using BazaarPlusPlus.GameInterop.Events;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.SteamTimeline;

[HarmonyPatch(typeof(BazaarVFXManager), "CombatStarted")]
internal static class SteamTimelineCombatStartedPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (AppState.CurrentState is not PVPCombatState)
            return;

        BppPatchHost.Services.EventBus.Publish(LivePvpPlaybackStartedObserved.Instance);
    }
}

[HarmonyPatch(typeof(BazaarVFXManager), "CombatPlaybackDone")]
internal static class SteamTimelineCombatPlaybackDonePatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        if (AppState.CurrentState is not PVPCombatState)
            return;

        BppPatchHost.Services.EventBus.Publish(LivePvpPlaybackEndedObserved.Instance);
    }
}
