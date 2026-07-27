#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Upload;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal static class BazaarDbSnapshotUploadSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create(IBppEventBus eventBus)
    {
        if (eventBus == null)
            throw new ArgumentNullException(nameof(eventBus));

        return CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.BazaarDbUpload,
            "BazaarDbUpload",
            BazaarDbSnapshotUploadSettingsMenuLabel.Resolve,
            ReadEnabled,
            WriteEnabled,
            enabled => OnEnabledChanged(eventBus, enabled)
        );
    }

    private static bool ReadEnabled(IBppConfig config) =>
        config.BazaarDbUploadEnabled?.Value ?? false;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.BazaarDbUploadEnabled;
        if (entry != null)
            entry.Value = enabled;

        if (enabled)
            EndOfRunScreenshotSettingsPolicy.ForceEnabled(config);
    }

    private static void OnEnabledChanged(IBppEventBus eventBus, bool enabled)
    {
        if (!enabled)
            return;

        eventBus.Publish(new UploadArmRequested(UploadFeedKind.BazaarDbSnapshot));
    }
}
