#pragma warning disable CS0436
using System;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class CombatStatusBarSettingsAwakePatch
{
    private static readonly SettingsMenuToggleDefinition Definition = new(
        "BPP_CombatStatusBarToggle",
        "CombatStatusBar",
        CombatStatusBarSettingsMenuLabel.Resolve,
        new CombatStatusBarSettingsMenuBridge(
            CombatStatusBar.GetEnabledSettingValue,
            CombatStatusBar.SetEnabledSettingValue
        )
    );

    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            BppGameplaySettingsCoordinator.EnsureAll(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatStatusBar", "Failed to add settings toggle", ex);
        }
    }

    internal static void EnsureToggleExists(OptionsDialogController instance)
    {
        SettingsMenuToggleInstaller.EnsureToggleExists(instance, Definition);
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class CombatStatusBarSettingsOnEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            BppGameplaySettingsCoordinator.EnsureAll(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatStatusBar", "Failed to sync settings toggle", ex);
        }
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnGameplayButtonClick")]
internal static class CombatStatusBarSettingsGameplayOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            BppGameplaySettingsCoordinator.EnsureAll(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CombatStatusBar",
                "Failed to refresh settings toggles after gameplay menu opened",
                ex
            );
        }
    }
}
