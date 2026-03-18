#pragma warning disable CS0436

namespace BazaarPlusPlus;

internal static class BppGameplaySettingsCoordinator
{
    private static readonly string[] ToggleOrder =
    [
        "BPP_NameOverrideToggle",
        "BPP_EnchantPreviewToggle",
        "BPP_CombatStatusBarToggle",
    ];

    internal static void EnsureAll(OptionsDialogController instance)
    {
        NameOverrideSettingsAwakePatch.EnsureToggleExists(instance);
        EnchantPreviewSettingsAwakePatch.EnsureToggleExists(instance);
        CombatStatusBarSettingsAwakePatch.EnsureToggleExists(instance);
        SettingsMenuToggleInstaller.ArrangeRows(instance, ToggleOrder);
    }
}
