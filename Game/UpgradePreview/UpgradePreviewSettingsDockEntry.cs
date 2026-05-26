#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.UpgradePreview;

internal sealed class UpgradePreviewSettingsDockEntry : ISettingsDockEntry
{
    public int Order => 4;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "UpgradePreview",
            UpgradePreviewSettingsMenuLabel.Resolve,
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
        config.UpgradePreviewModeConfig?.Value ?? PreviewVisibilityMode.AutoOnPedestalChoice;

    private static bool IsOverrideActive(IBppConfig config) =>
        ReadMode(config) != PreviewVisibilityMode.Off;

    private static void CycleMode(IBppConfig config)
    {
        var entry = config.UpgradePreviewModeConfig;
        if (entry != null)
            entry.Value = BppSettingsDockCatalog.NextPreviewVisibilityMode(entry.Value);
    }
}
