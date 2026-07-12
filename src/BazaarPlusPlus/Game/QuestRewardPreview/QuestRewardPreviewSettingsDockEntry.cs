#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.QuestRewardPreview;

internal static class QuestRewardPreviewSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.QuestRewardPreview,
            "QuestRewardPreview",
            QuestRewardPreviewSettingsMenuLabel.Resolve,
            config => config.EnableQuestRewardPreviewConfig?.Value ?? false,
            (config, enabled) =>
            {
                var entry = config.EnableQuestRewardPreviewConfig;
                if (entry != null)
                    entry.Value = enabled;
            }
        );
}
