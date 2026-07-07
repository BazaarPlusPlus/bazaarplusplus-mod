#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EventPreviewSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.EventPreview;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "EventPreview",
            EventPreviewSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => config.EnableEventPreviewConfig?.Value ?? true,
                enabled =>
                {
                    var entry = config.EnableEventPreviewConfig;
                    if (entry != null)
                        entry.Value = enabled;
                }
            )
        );
}
