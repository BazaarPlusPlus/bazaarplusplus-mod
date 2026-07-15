#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.CombatReplay;
using HarmonyLib;
using TheBazaar;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch(
    typeof(BoardRecapReplayButtonsController),
    nameof(BoardRecapReplayButtonsController.Show)
)]
internal static class CurrentReplayRecordingButtonPatch
{
    private static readonly FieldInfo? ReplayButtonField = AccessTools.Field(
        typeof(BoardRecapReplayButtonsController),
        "ReplayButton"
    );
    private static readonly FieldInfo? ContainerField = AccessTools.Field(
        typeof(BoardRecapReplayButtonsController),
        "RecapReplayButtonContainer"
    );

    [HarmonyPostfix]
    private static void Postfix(BoardRecapReplayButtonsController __instance)
    {
        if (__instance == null)
            return;
        var replayButton = ReplayButtonField?.GetValue(__instance) as Button;
        var container = ContainerField?.GetValue(__instance) as GameObject;
        if (replayButton == null || container == null)
            return;

        var controller = __instance.GetComponent<CurrentReplayRecordingButtonController>();
        if (controller == null)
            controller =
                __instance.gameObject.AddComponent<CurrentReplayRecordingButtonController>();
        controller.Bind(replayButton, container.transform);
    }
}
