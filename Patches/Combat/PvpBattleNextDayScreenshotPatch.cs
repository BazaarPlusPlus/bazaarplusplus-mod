#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(BoardRecapReplayButtonsController), "Continue")]
internal static class PvpBattleNextDayScreenshotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(BoardRecapReplayButtonsController __instance)
    {
        if (EndOfRunScreenshotController.TryConsumePvpBattleContinuePassthrough())
            return true;
        if (EndOfRunScreenshotController.ShouldSuppressPvpBattleContinueWhileCaptureInFlight())
            return false;

        return !EndOfRunScreenshotController.TryCapturePvpBattleNextDayContinue(__instance);
    }
}
