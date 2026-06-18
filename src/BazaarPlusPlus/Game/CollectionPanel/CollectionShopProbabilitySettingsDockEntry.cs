#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionShopProbabilitySettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.CollectionShopProbability;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "CollectionShopProbability",
            CollectionPanelText.ShopProbabilityToggleLabel,
            new SettingsMenuToggleBridge(
                () => ReadEnabled(config),
                enabled => WriteEnabled(config, enabled),
                _ => CollectionPanel.NotifyShopProbabilityToggled()
            )
        );

    private static bool ReadEnabled(IBppConfig config) =>
        config.EnableCollectionShopProbabilityConfig?.Value ?? false;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.EnableCollectionShopProbabilityConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}
