#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelAccessPolicy
{
    internal static bool CanOpen(bool isInCombat, bool communityContributionEnabled)
    {
        return !isInCombat && communityContributionEnabled;
    }
}
