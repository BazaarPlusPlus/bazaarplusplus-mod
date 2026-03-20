#pragma warning disable CS0436
using System;
using HarmonyLib;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(OptionsDialogController), "OnLanguageOptionChanged")]
internal static class OptionsDialogLanguageRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            BppGameplaySettingsCoordinator.EnsureAll(__instance);
            BppKeybindSettingsAwakePatch.RefreshLanguage(__instance);
            NativeKeybindLabelAwakePatch.TryUpdateLabels(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "SettingsMenu",
                "Failed to refresh custom settings after language changed",
                ex
            );
        }
    }
}
