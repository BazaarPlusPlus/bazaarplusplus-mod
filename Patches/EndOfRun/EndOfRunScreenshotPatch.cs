#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using HarmonyLib;
using TheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(EndOfRunScreenController), "OnContinueClick")]
internal static class EndOfRunScreenshotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EndOfRunScreenController __instance)
    {
        if (EndOfRunScreenshotController.TryConsumeContinuePassthrough())
            return true;

        if (EndOfRunScreenshotController.ShouldSuppressContinueWhileCaptureInFlight())
            return false;

        if (EndOfRunScreenshotController.ShouldBlockContinueUntilFirstCapture(__instance))
            return false;

        if (!EndOfRunContinueStateEvaluator.TryIsInteractionBlocked(__instance, out var isInteractionBlocked))
        {
            BppLog.Warn(
                "EndOfRunScreenshot",
                $"ContinuePatch action=allow-reflection-fallback frame={UnityEngine.Time.frameCount} time={UnityEngine.Time.unscaledTime:F3} {EndOfRunContinueStateEvaluator.DescribeState(__instance, EndOfRunScreenshotController.ShouldSuppressContinueWhileCaptureInFlight())}"
            );
            return true;
        }

        var captureIntercepted = EndOfRunScreenshotController.TryCaptureFirstContinue(
            __instance,
            isInteractionBlocked
        );
        return !captureIntercepted;
    }
}
