#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.BazaarDbUpload;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "BazaarDbUpload",
            BazaarDbSnapshotUploadSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => ReadEnabled(config),
                enabled => WriteEnabled(config, enabled),
                OnEnabledChanged
            )
        );

    private static bool ReadEnabled(IBppConfig config) =>
        config.BazaarDbUploadEnabled?.Value ?? false;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.BazaarDbUploadEnabled;
        if (entry != null)
            entry.Value = enabled;
    }

    private static void OnEnabledChanged(bool enabled)
    {
        if (!enabled)
            return;

        BppLog.Info(
            BazaarDbSnapshotUploadFeed.BazaarDbSnapshotScope,
            "BazaarDB screenshot upload toggle armed an immediate attempt."
        );
        BackgroundUploadPump.ArmImmediate(BazaarDbSnapshotUploadFeed.BazaarDbSnapshotScope);
    }
}
