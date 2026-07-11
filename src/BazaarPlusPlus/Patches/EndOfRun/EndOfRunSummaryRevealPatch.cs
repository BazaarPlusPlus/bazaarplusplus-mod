#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using HarmonyLib;
using TheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus.Patches.EndOfRun;

[HarmonyPatch(typeof(EndOfRunSummaryController), "DisplayCardsAsync")]
internal static class EndOfRunSummaryRevealPatch
{
    [HarmonyPrefix]
    private static void Prefix(EndOfRunSummaryController __instance)
    {
        // This method is invoked only after the native carpet unlock has completed and the card
        // container is visible; skill display follows in the same OnLoadShowCards call. A prefix
        // is intentional: an async postfix would run at the first suspension, not completion.
        EndOfRunScreenshotController.NotifySummaryRevealStarted(__instance);
    }
}
