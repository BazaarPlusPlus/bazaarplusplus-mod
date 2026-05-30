#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanelSettingsDockEntry : ISettingsDockEntry
{
    // HistoryPanel uses Order = 0; the collection panel sits immediately after so the two
    // related entry-points stay grouped in the dock.
    public int Order => 1;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "CardCollection",
            CollectionPanelSettingsMenuLabel.Resolve,
            ResolveStatus,
            IsActionable,
            CollectionPanel.OpenFromDockEntry,
            collapseAfterActivate: true
        );

    private static string ResolveStatus(string languageCode)
    {
        if (TheBazaar.Data.IsInCombat)
            return CollectionPanelSettingsMenuLabel.ResolveInRunStatus(languageCode);

        return CollectionPanel.IsVisible
            ? CollectionPanelSettingsMenuLabel.ResolveOpenStatus(languageCode)
            : CollectionPanelSettingsMenuLabel.ResolveViewStatus(languageCode);
    }

    private static bool IsActionable() => !TheBazaar.Data.IsInCombat;
}
