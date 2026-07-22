#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal readonly record struct ReplayVideoCombatPosition(int Frame, int CombatMs);

internal sealed record ReplayVideoSyncAnchor(
    string RecordingId,
    string BattleId,
    int CombatFrame,
    int CombatMs,
    long MediaPtsMs,
    long OutputOrdinal
);

internal sealed class ReplayVideoCombatPositionTracker
{
    private readonly int _frameDurationMs;
    private int _lastCompletedFrame = -1;

    internal ReplayVideoCombatPositionTracker(int frameDurationMs)
    {
        if (frameDurationMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameDurationMs));
        _frameDurationMs = frameDurationMs;
    }

    internal void Reset() => _lastCompletedFrame = -1;

    internal void Advance()
    {
        if (_lastCompletedFrame < int.MaxValue)
            _lastCompletedFrame++;
    }

    internal ReplayVideoCombatPosition Snapshot()
    {
        var frame = Math.Max(0, _lastCompletedFrame);
        return new ReplayVideoCombatPosition(frame, checked(frame * _frameDurationMs));
    }
}

internal sealed class ReplayVideoSyncAnchorCollector
{
    private readonly string _recordingId;
    private readonly string _battleId;
    private readonly int _fps;
    private ReplayVideoCombatPosition _latestPosition;
    private long _latestSequence;
    private long _nextOutputOrdinal;
    private bool _hasLatest;

    internal ReplayVideoSyncAnchorCollector(string recordingId, string battleId, int fps)
    {
        _recordingId = recordingId ?? throw new ArgumentNullException(nameof(recordingId));
        _battleId = battleId ?? throw new ArgumentNullException(nameof(battleId));
        if (fps <= 0)
            throw new ArgumentOutOfRangeException(nameof(fps));
        _fps = fps;
    }

    internal bool TryAdoptCapturedFrame(long sequence, ReplayVideoCombatPosition position)
    {
        if (sequence <= 0 || (_hasLatest && sequence <= _latestSequence))
            return false;
        _latestSequence = sequence;
        _latestPosition = position;
        _hasLatest = true;
        return true;
    }

    internal ReplayVideoSyncAnchor RecordOutput()
    {
        if (!_hasLatest)
            throw new InvalidOperationException("A captured source frame is required.");
        var ordinal = _nextOutputOrdinal++;
        return new ReplayVideoSyncAnchor(
            _recordingId,
            _battleId,
            _latestPosition.Frame,
            _latestPosition.CombatMs,
            checked(ordinal * 1000L / _fps),
            ordinal
        );
    }
}

internal static class ReplayVideoSyncMetadata
{
    internal static bool IsValidAnchorSequence(
        string recordingId,
        string battleId,
        IReadOnlyList<ReplayVideoSyncAnchor>? anchors
    )
    {
        if (anchors == null)
            return false;

        ReplayVideoSyncAnchor? previous = null;
        for (var index = 0; index < anchors.Count; index++)
        {
            var anchor = anchors[index];
            if (
                anchor == null
                || !string.Equals(anchor.RecordingId, recordingId, StringComparison.Ordinal)
                || !string.Equals(anchor.BattleId, battleId, StringComparison.Ordinal)
                || anchor.CombatFrame < 0
                || anchor.CombatMs < 0
                || anchor.MediaPtsMs < 0
                || anchor.OutputOrdinal != index
            )
            {
                return false;
            }

            if (
                previous != null
                && (
                    anchor.MediaPtsMs < previous.MediaPtsMs
                    || anchor.CombatFrame < previous.CombatFrame
                    || anchor.CombatMs < previous.CombatMs
                )
            )
            {
                return false;
            }

            previous = anchor;
        }

        return true;
    }

    internal static IReadOnlyList<ReplayVideoSyncAnchor> SelectExactAnchors(
        string recordingId,
        string battleId,
        IReadOnlyList<ReplayVideoSyncAnchor>? anchors
    )
    {
        if (
            anchors == null
            || anchors.Count < 2
            || !IsValidAnchorSequence(recordingId, battleId, anchors)
        )
            return Array.Empty<ReplayVideoSyncAnchor>();

        var exact = new List<ReplayVideoSyncAnchor>(anchors.Count);
        for (var index = 0; index < anchors.Count; index++)
            exact.Add(anchors[index]);

        return exact;
    }
}
