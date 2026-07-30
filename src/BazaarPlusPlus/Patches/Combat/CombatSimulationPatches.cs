#nullable enable
#pragma warning disable CS0436
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.GameInterop.Events;
using BazaarPlusPlus.GameInterop.Recap;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Combat;

// Combat sim: capture win/loss result
[HarmonyPatch(typeof(CombatSimHandler), "Simulate")]
class CombatSimPatch
{
    [HarmonyPrefix]
    static void Prefix(NetMessageCombatSim message, CancellationTokenSource cancellationToken)
    {
        BppPatchHost.Services.EventBus.Publish(new CombatSimObserved { Message = message });
    }
}

[HarmonyPatch(typeof(FinalBlowSlowDownController), nameof(FinalBlowSlowDownController.Process))]
class CombatFrameAdvancePatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        BppPatchHost.Services.EventBus.Publish(CombatFrameAdvanced.Instance);
    }
}

[HarmonyPatch(
    typeof(BoardRecapReplayButtonsController),
    nameof(BoardRecapReplayButtonsController.Show)
)]
internal static class NativeRecapControlsPatch
{
    [HarmonyPostfix]
    private static void Postfix(BoardRecapReplayButtonsController __instance)
    {
        NativeRecapControls.Observe(__instance);
    }
}
