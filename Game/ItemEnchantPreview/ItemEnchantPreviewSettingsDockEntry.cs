#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal sealed class ItemEnchantPreviewSettingsDockEntry : ISettingsDockEntry
{
    public int Order => 3;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "EnchantPreview",
            EnchantPreviewSettingsMenuLabel.Resolve,
            languageCode =>
                BppSettingsDockCatalog.ResolvePreviewVisibilityModeStatus(
                    ReadMode(config),
                    languageCode
                ),
            () => IsOverrideActive(config),
            () => CycleMode(config),
            collapseAfterActivate: false
        );

    private static PreviewVisibilityMode ReadMode(IBppConfig config) =>
        config.EnchantPreviewModeConfig?.Value ?? PreviewVisibilityMode.AutoOnPedestalChoice;

    private static bool IsOverrideActive(IBppConfig config) =>
        ReadMode(config) != PreviewVisibilityMode.Off;

    private static void CycleMode(IBppConfig config)
    {
        var entry = config.EnchantPreviewModeConfig;
        if (entry != null)
            entry.Value = BppSettingsDockCatalog.NextPreviewVisibilityMode(entry.Value);
    }
}
