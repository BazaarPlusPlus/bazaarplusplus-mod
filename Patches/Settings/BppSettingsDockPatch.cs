#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;
using UnityEngine.UI;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(SettingDialogsView), "Awake")]
internal static class BppSettingsDockAwakePatch
{
    private static readonly System.Reflection.FieldInfo? MainMenuSettingOptionButtonField =
        AccessTools.Field(typeof(SettingDialogsView), "MainMenuSettingOptionButton");

    private static readonly System.Reflection.FieldInfo? HeroSelectSettingOptionButtonField =
        AccessTools.Field(typeof(SettingDialogsView), "HeroSelectSettingOptionButton");

    [HarmonyPostfix]
    private static void Postfix(SettingDialogsView __instance)
    {
        try
        {
            AttachDock(MainMenuSettingOptionButtonField?.GetValue(__instance) as Button);
            AttachDock(HeroSelectSettingOptionButtonField?.GetValue(__instance) as Button);
        }
        catch (Exception ex)
        {
            BppLog.Error("BppSettingsDock", "Failed to attach BPP settings dock", ex);
        }
    }

    private static void AttachDock(Button? button)
    {
        if (button == null)
            return;

        BppSettingsDockController.Attach(button);
    }
}
