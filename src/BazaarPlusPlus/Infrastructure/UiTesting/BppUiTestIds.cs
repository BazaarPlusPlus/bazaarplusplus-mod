#nullable enable
namespace BazaarPlusPlus.Infrastructure.UiTesting;

/// <summary>
/// Stable selectors for plugin-owned Unity UI. UI Toolkit elements use their <c>name</c>
/// property; uGUI surfaces use the owning GameObject name.
/// </summary>
internal static class BppUiTestIds
{
    public const string HistoryRootObject = "BPP_HistoryPanelUiToolkitRoot";
    public const string HistoryRoot = "bpp-history-root";
    public const string HistoryRunsSection = "bpp-history-runs-section";
    public const string HistoryBattlesSection = "bpp-history-battles-section";
    public const string HistoryRunsList = "bpp-history-runs-list";
    public const string HistoryBattlesList = "bpp-history-battles-list";
    public const string HistoryPreview = "bpp-history-preview";
    public const string HistoryRunsTab = "bpp-history-runs-tab";
    public const string HistoryGhostTab = "bpp-history-ghost-tab";
    public const string HistoryStatus = "bpp-history-status";
    public const string HistoryClose = "bpp-history-close";
    public const string HistoryReplay = "bpp-history-replay";
    public const string HistoryDetailedReport = "bpp-history-detailed-report";
    public const string HistoryRecord = "bpp-history-record";
    public const string HistoryDelete = "bpp-history-delete";

    public const string CurrentReplayRecord = "BPP_CurrentReplayRecordingButton";
    public const string CurrentReplayRecordAgain = "BPP_CurrentReplayRecordAgainButton";
}
