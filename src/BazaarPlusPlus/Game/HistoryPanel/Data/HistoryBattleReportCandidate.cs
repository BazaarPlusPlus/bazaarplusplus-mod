#nullable enable
using BazaarPlusPlus.GameInterop.Files;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

/// <summary>
/// A completed recording row that may own an immutable static report. Candidates are returned
/// newest-first by storage; the report path is derived only from the canonical recording ID.
/// </summary>
internal sealed class HistoryBattleReportCandidate
{
    public HistoryBattleReportCandidate(
        string recordingId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc
    )
    {
        RecordingId = recordingId ?? string.Empty;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
    }

    public string RecordingId { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? EndedAtUtc { get; }

    internal bool TryGetTypedRecordingId(out CombatReportRecordingId recordingId) =>
        CombatReportRecordingId.TryParse(RecordingId, out recordingId);
}
