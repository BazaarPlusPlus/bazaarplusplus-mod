#pragma warning disable CS0436
using System;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class NameOverrideSettingsAwakePatch
{
    private static readonly SettingsMenuToggleDefinition Definition = new(
        "BPP_NameOverrideToggle",
        "NameOverride",
        NameOverrideSettingsMenuLabel.Resolve,
        new NameOverrideSettingsMenuBridge(
            ReadEnabledValue,
            WriteEnabledValue,
            NameOverrideUiRefresh.TryRefreshVisibleHeroBanners
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
            BppLog.Error("NameOverride", "Failed to add settings toggle", ex);
        }
    }

    internal static void EnsureToggleExists(OptionsDialogController instance)
    {
        SettingsMenuToggleInstaller.EnsureToggleExists(instance, Definition);
    }

    private static bool ReadEnabledValue()
    {
        var entry = ModState.EnableNameOverrideConfig;
        return entry != null && entry.Value;
    }

    private static void WriteEnabledValue(bool enabled)
    {
        var entry = ModState.EnableNameOverrideConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class NameOverrideSettingsOnEnablePatch
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
            BppLog.Error("NameOverride", "Failed to sync settings toggle", ex);
        }
    }
}
