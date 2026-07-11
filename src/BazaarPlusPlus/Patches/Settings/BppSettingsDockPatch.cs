#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using UnityEngine.UI;

namespace BazaarPlusPlus.Patches.Settings;

[HarmonyPatch(typeof(SettingDialogsView), "Awake")]
internal static class CollectionPanelDockButtonAwakePatch
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
            AttachButton(
                MainMenuSettingOptionButtonField?.GetValue(__instance) as Button,
                "MainMenu"
            );
            AttachButton(
                HeroSelectSettingOptionButtonField?.GetValue(__instance) as Button,
                "HeroSelect"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("CollectionPanelDockButton", "Failed to attach collection dock button", ex);
        }
    }

    private static void AttachButton(Button? button, string key)
    {
        if (button == null)
            return;

        CollectionPanelDockButtonController.Attach(
            button,
            BppSettingsDockPlacement.ForButton(
                $"CollectionPanel_{key}",
                BppDockButtonIconKind.CollectionPanel
            )
        );
    }
}

[HarmonyPatch(typeof(FightMenuDialog), "Start")]
internal static class CollectionPanelDockButtonFightMenuPatch
{
    private static readonly System.Reflection.FieldInfo? SettingButtonField = AccessTools.Field(
        typeof(FightMenuDialog),
        "SettingButton"
    );

    [HarmonyPostfix]
    private static void Postfix(FightMenuDialog __instance)
    {
        try
        {
            var settingButtonCustom = SettingButtonField?.GetValue(__instance) as ButtonCustom;
            var button = settingButtonCustom?.GetButton();
            if (button != null)
            {
                CollectionPanelDockButtonController.Attach(
                    button,
                    BppSettingsDockPlacement.ForButton(
                        "CollectionPanel_FightMenu",
                        BppDockButtonIconKind.CollectionPanel
                    )
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CollectionPanelDockButton",
                "Failed to attach collection dock button in fight menu",
                ex
            );
        }
    }
}
