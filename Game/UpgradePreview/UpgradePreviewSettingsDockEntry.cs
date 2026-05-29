#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BepInEx.Configuration;

namespace BazaarPlusPlus.Game.UpgradePreview;

internal sealed class UpgradePreviewSettingsDockEntry : PreviewVisibilityModeDockEntry
{
    public override int Order => 4;

    protected override string Key => "UpgradePreview";

    protected override Func<string, string> ResolveLabel =>
        UpgradePreviewSettingsMenuLabel.Resolve;

    protected override ConfigEntry<PreviewVisibilityMode>? GetModeConfig(IBppConfig config) =>
        config.UpgradePreviewModeConfig;
}
