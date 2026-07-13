#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Patches.Tooltips;

namespace BazaarPlusPlus.Game.QuestPreview;

internal static class QuestPreviewSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create(Action? clearPooledTooltips = null) =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.QuestPreview,
            "QuestPreview",
            QuestPreviewSettingsMenuLabel.Resolve,
            config => config.EnableQuestPreviewConfig?.Value ?? false,
            (config, enabled) =>
            {
                var entry = config.EnableQuestPreviewConfig;
                if (entry != null)
                    entry.Value = enabled;
            },
            enabled =>
            {
                if (enabled)
                    return;

                if (clearPooledTooltips != null)
                    clearPooledTooltips();
                else
                    QuestPreviewPooledTooltipCleanup.Clear();
            }
        );
}
