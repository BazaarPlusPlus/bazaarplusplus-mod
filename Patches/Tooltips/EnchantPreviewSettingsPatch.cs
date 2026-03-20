#pragma warning disable CS0436
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class EnchantPreviewSettingsAwakePatch
{
    private static readonly SettingsMenuToggleDefinition Definition = new(
        "BPP_EnchantPreviewToggle",
        "EnchantPreview",
        EnchantPreviewSettingsMenuLabel.Resolve,
        new SettingsMenuToggleBridge(ReadEnabledValue, WriteEnabledValue),
        "BPP_CombatStatusBarToggle"
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
            BppLog.Error("EnchantPreview", "Failed to add settings toggle", ex);
        }
    }

    internal static void EnsureToggleExists(OptionsDialogController instance)
    {
        SettingsMenuToggleInstaller.EnsureToggleExists(instance, Definition);
    }

    private static bool ReadEnabledValue()
    {
        var entry = BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig;
        return entry != null && entry.Value;
    }

    private static void WriteEnabledValue(bool enabled)
    {
        var entry = BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class EnchantPreviewSettingsOnEnablePatch
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
            BppLog.Error("EnchantPreview", "Failed to sync settings toggle", ex);
        }
    }
}
