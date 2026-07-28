#nullable enable
using BazaarPlusPlus.Game.CombatReplay.Video;

namespace BazaarPlusPlus.Game.CombatReplay;

internal enum CurrentReplayRecordingPhase
{
    Unavailable,
    AwaitingBattlePersistence,
    Preparing,
    Ready,
    Armed,
    Recording,
    Finalizing,
    Succeeded,
    Degraded,
    Failed,
}

internal readonly record struct CurrentReplayRecordingSnapshot(
    CurrentReplayRecordingPhase Phase,
    string? BattleId,
    string? RecordingId,
    string? FinalFilePath,
    string? ReportHtmlFilePath,
    string? Reason,
    bool Visible,
    bool CanStart,
    bool CanReveal,
    bool CanOpenReport
);

internal sealed class CurrentReplayRecordingState
{
    private string? _battleId;
    private string? _recordingId;
    private string? _artifactRecordingId;
    private string? _finalFilePath;
    private string? _reportHtmlFilePath;
    private string? _reason;
    private CombatReplayPlaybackSource? _source;
    private bool _battlePersisted;
    private bool _replayStateActive;
    private bool _availabilityReady;
    private bool _nativeReplayStarted;

    internal CurrentReplayRecordingPhase Phase { get; private set; } =
        CurrentReplayRecordingPhase.Unavailable;

    internal string? BattleId => _battleId;
    internal string? RecordingId => _recordingId;
    internal bool NativeReplayStarted => _nativeReplayStarted;
    internal bool HasActiveSession =>
        Phase
            is CurrentReplayRecordingPhase.Armed
                or CurrentReplayRecordingPhase.Recording
                or CurrentReplayRecordingPhase.Finalizing;

    internal void LatchBattle(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));

        _battleId = battleId;
        _recordingId = null;
        _artifactRecordingId = null;
        _finalFilePath = null;
        _reportHtmlFilePath = null;
        _reason = null;
        _source = CombatReplayPlaybackSource.CurrentNative;
        _battlePersisted = false;
        _availabilityReady = false;
        _nativeReplayStarted = false;
        Phase = CurrentReplayRecordingPhase.AwaitingBattlePersistence;
    }

    internal void TrackManagedReplay(string battleId, CombatReplayPlaybackSource source)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));
        if (source == CombatReplayPlaybackSource.CurrentNative)
            throw new ArgumentException(
                "Native replay recordings must be latched from the current battle.",
                nameof(source)
            );

        _battleId = battleId;
        _recordingId = null;
        _artifactRecordingId = null;
        _finalFilePath = null;
        _reportHtmlFilePath = null;
        _reason = null;
        _source = source;
        _battlePersisted = true;
        _availabilityReady = false;
        _nativeReplayStarted = false;
        Phase = CurrentReplayRecordingPhase.Preparing;
    }

    internal void EnterReplayState()
    {
        _replayStateActive = true;
        RefreshReadyPhase();
    }

    internal void MarkBattlePersistence(string battleId, bool succeeded, string? reason)
    {
        if (!MatchesBattle(battleId) || HasActiveSession)
            return;

        _battlePersisted = succeeded;
        _reason = succeeded ? null : reason;
        Phase = succeeded
            ? CurrentReplayRecordingPhase.Preparing
            : CurrentReplayRecordingPhase.Failed;
        RefreshReadyPhase();
    }

    internal void SetAvailability(bool ready, string? reason)
    {
        if (_battleId == null || HasActiveSession)
            return;

        if (Phase is CurrentReplayRecordingPhase.Succeeded or CurrentReplayRecordingPhase.Degraded)
        {
            _availabilityReady = ready;
            return;
        }

        _availabilityReady = ready;
        if (!ready)
        {
            _reason = reason;
            if (_battlePersisted && Phase != CurrentReplayRecordingPhase.Failed)
                Phase = CurrentReplayRecordingPhase.Preparing;
            return;
        }

        if (Phase == CurrentReplayRecordingPhase.Failed)
            return;

        _reason = null;
        RefreshReadyPhase();
    }

    internal bool TryArm(string recordingId)
    {
        if (
            string.IsNullOrWhiteSpace(recordingId)
            || _source != CombatReplayPlaybackSource.CurrentNative
            || !_replayStateActive
            || !_battlePersisted
            || !_availabilityReady
            || Phase
                is not (
                    CurrentReplayRecordingPhase.Ready
                    or CurrentReplayRecordingPhase.Succeeded
                    or CurrentReplayRecordingPhase.Degraded
                    or CurrentReplayRecordingPhase.Failed
                )
        )
        {
            return false;
        }

        _recordingId = recordingId;
        _reason = null;
        _nativeReplayStarted = false;
        Phase = CurrentReplayRecordingPhase.Armed;
        return true;
    }

    internal bool MarkNativeReplayStarted()
    {
        if (
            _source != CombatReplayPlaybackSource.CurrentNative
            || Phase != CurrentReplayRecordingPhase.Armed
            || _recordingId == null
        )
            return false;

        _nativeReplayStarted = true;
        return true;
    }

    internal void MarkRecordingStarted(
        string recordingId,
        string battleId,
        CombatReplayPlaybackSource source
    )
    {
        if (!MatchesBattle(battleId) || _source != source)
            return;

        if (source == CombatReplayPlaybackSource.CurrentNative)
        {
            if (!MatchesSession(recordingId, battleId) || !_nativeReplayStarted)
                return;
        }
        else
        {
            if (
                !string.IsNullOrWhiteSpace(_recordingId)
                && !string.Equals(_recordingId, recordingId, StringComparison.Ordinal)
            )
            {
                return;
            }

            _recordingId = recordingId;
        }

        Phase = CurrentReplayRecordingPhase.Recording;
        _reason = null;
    }

    internal void RollbackArm(string recordingId, string reason)
    {
        if (!string.Equals(_recordingId, recordingId, StringComparison.Ordinal))
            return;

        _recordingId = null;
        _nativeReplayStarted = false;
        _reason = reason;
        Phase =
            _battlePersisted && _availabilityReady
                ? CurrentReplayRecordingPhase.Ready
                : CurrentReplayRecordingPhase.Failed;
    }

    internal void MarkReplayEnded(string? reason = null)
    {
        if (!_nativeReplayStarted)
            return;

        _nativeReplayStarted = false;
        if (Phase == CurrentReplayRecordingPhase.Recording)
        {
            Phase = CurrentReplayRecordingPhase.Finalizing;
            _reason = reason;
            return;
        }

        if (Phase == CurrentReplayRecordingPhase.Armed)
        {
            Phase = CurrentReplayRecordingPhase.Failed;
            _reason = reason ?? "Video recording failed to start.";
        }
    }

    internal void ApplyCompletion(CombatReplayVideoRecordingCompleted completion)
    {
        if (
            completion.Source != _source
            || !MatchesSession(completion.RecordingId, completion.BattleId)
        )
        {
            return;
        }

        if (completion.ArtifactUsable)
        {
            _artifactRecordingId = completion.RecordingId;
            _finalFilePath = completion.FinalFilePath;
            // The previous report belongs to the previous video artifact. Until publication
            // for this replacement terminates, the primary action falls back to its MP4.
            _reportHtmlFilePath = null;
        }
        _reason = completion.Reason;
        _nativeReplayStarted = false;
        if (!completion.ArtifactUsable)
        {
            Phase = CurrentReplayRecordingPhase.Failed;
            return;
        }

        Phase =
            completion.MetadataStatus == ReplayVideoMetadataStatus.Complete
                ? completion.ReasonCode == ReplayVideoRecordingReasonCode.Completed
                    ? CurrentReplayRecordingPhase.Succeeded
                    : CurrentReplayRecordingPhase.Degraded
                : CurrentReplayRecordingPhase.Degraded;
    }

    internal void ApplyReportCompletion(
        string recordingId,
        string battleId,
        string reportHtmlFilePath
    )
    {
        if (
            string.IsNullOrWhiteSpace(reportHtmlFilePath)
            || !MatchesBattle(battleId)
            || !string.Equals(_artifactRecordingId, recordingId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(_finalFilePath)
        )
        {
            return;
        }

        _reportHtmlFilePath = reportHtmlFilePath;
    }

    internal void LeaveReplayState()
    {
        Reset();
    }

    internal CurrentReplayRecordingSnapshot Snapshot()
    {
        var visible = _replayStateActive && _battleId != null;
        var canReveal = visible && !string.IsNullOrWhiteSpace(_finalFilePath) && !HasActiveSession;
        var canOpenReport =
            visible && !string.IsNullOrWhiteSpace(_reportHtmlFilePath) && !HasActiveSession;
        var canStart =
            visible
            && _source == CombatReplayPlaybackSource.CurrentNative
            && _battlePersisted
            && _availabilityReady
            && Phase
                is CurrentReplayRecordingPhase.Ready
                    or CurrentReplayRecordingPhase.Succeeded
                    or CurrentReplayRecordingPhase.Degraded
                    or CurrentReplayRecordingPhase.Failed;
        return new CurrentReplayRecordingSnapshot(
            Phase,
            _battleId,
            _recordingId,
            _finalFilePath,
            _reportHtmlFilePath,
            _reason,
            visible,
            canStart,
            canReveal,
            canOpenReport
        );
    }

    private bool MatchesBattle(string battleId) =>
        string.Equals(_battleId, battleId, StringComparison.Ordinal);

    private bool MatchesSession(string recordingId, string battleId) =>
        MatchesBattle(battleId)
        && string.Equals(_recordingId, recordingId, StringComparison.Ordinal);

    private void RefreshReadyPhase()
    {
        if (_battleId == null || HasActiveSession || !_battlePersisted)
            return;

        Phase =
            _replayStateActive && _availabilityReady
                ? CurrentReplayRecordingPhase.Ready
                : CurrentReplayRecordingPhase.Preparing;
    }

    private void Reset()
    {
        _battleId = null;
        _recordingId = null;
        _artifactRecordingId = null;
        _finalFilePath = null;
        _reportHtmlFilePath = null;
        _reason = null;
        _source = null;
        _battlePersisted = false;
        _replayStateActive = false;
        _availabilityReady = false;
        _nativeReplayStarted = false;
        Phase = CurrentReplayRecordingPhase.Unavailable;
    }
}
