#nullable enable
using BazaarPlusPlus.GameInterop.Files;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

/// <summary>
/// A completed recording row that may own an immutable static report. Candidates are returned
/// newest-first by storage; the report path is derived only from the canonical recording ID.
/// </summary>
internal sealed class HistoryBattleReportCandidate
{
    public HistoryBattleReportCandidate(string recordingId) =>
        RecordingId = recordingId ?? string.Empty;

    public string RecordingId { get; }

    internal bool TryGetTypedRecordingId(out CombatReportRecordingId recordingId) =>
        CombatReportRecordingId.TryParse(RecordingId, out recordingId);
}
