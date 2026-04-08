#pragma warning disable CS0436
#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.Screenshots;
using HarmonyLib;
using TheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(EndOfRunScreenController), "OnContinueClick")]
internal static class EndOfRunScreenshotPatch
{
    private static readonly FieldInfo? TransitionCountField = AccessTools.Field(
        typeof(EndOfRunScreenController),
        "_transitionCount"
    );
    private static bool _warnedMissingTransitionField;

    [HarmonyPrefix]
    private static bool Prefix(EndOfRunScreenController __instance)
    {
        if (EndOfRunScreenshotController.TryConsumeContinuePassthrough())
            return true;
        if (EndOfRunScreenshotController.ShouldSuppressContinueWhileCaptureInFlight())
            return false;

        if (TransitionCountField == null)
        {
            if (!_warnedMissingTransitionField)
            {
                _warnedMissingTransitionField = true;
                BppLog.Warn(
                    "EndOfRunScreenshot",
                    "Failed to resolve EndOfRunScreenController._transitionCount; skipping screenshot interception."
                );
            }

            return true;
        }

        var transitionCount = (int?)TransitionCountField.GetValue(__instance) ?? 0;
        return !EndOfRunScreenshotController.TryCaptureFirstContinue(
            __instance,
            transitionCount > 0
        );
    }
}
