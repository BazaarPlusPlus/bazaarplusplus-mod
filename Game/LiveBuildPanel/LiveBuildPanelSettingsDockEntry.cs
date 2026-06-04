#nullable enable

using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal sealed class LiveBuildPanelSettingsDockEntry : ISettingsDockEntry
{
    public int Order => 1;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "LiveBuildPanel",
            _ => LiveBuildPanelText.Title(),
            _ => LiveBuildPanel.IsVisible ? "OPEN" : "CAPS",
            () => !TheBazaar.Data.IsInCombat,
            LiveBuildPanel.OpenFromDockEntry,
            collapseAfterActivate: true
        );
}
