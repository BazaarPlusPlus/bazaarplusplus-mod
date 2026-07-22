#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop.Files;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// Resolves the first usable immutable report from newest-first completed recording rows. It never
/// globs and never derives a report path from the recorder-owned MP4 path.
/// </summary>
internal static class HistoryBattleReportLocator
{
    internal static bool TryResolveLatestReport(
        IReadOnlyList<HistoryBattleReportCandidate> candidates,
        string reportRootDirectoryPath,
        out ResolvedSystemReport? resolvedReport
    )
    {
        resolvedReport = null;
        if (candidates == null || string.IsNullOrWhiteSpace(reportRootDirectoryPath))
            return false;

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (
                candidate == null
                || !candidate.TryGetTypedRecordingId(out var recordingId)
                || !SystemReportOpener.TryResolve(
                    reportRootDirectoryPath,
                    recordingId,
                    out var report,
                    out _
                )
            )
            {
                continue;
            }

            resolvedReport = report;
            return true;
        }

        return false;
    }
}
