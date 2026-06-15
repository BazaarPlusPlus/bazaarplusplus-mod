#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Supporters;

internal sealed class FixedSupporterListSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.FixedSupporterList;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "StreamMode",
            FixedSupporterListSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => ReadEnabled(config),
                enabled => WriteEnabled(config, enabled)
            )
        );

    private static bool ReadEnabled(IBppConfig config) =>
        config.UseFixedSupporterListConfig?.Value
        ?? BPPSupporterListSourcePolicy.DefaultUseFixedList;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.UseFixedSupporterListConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}
