#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;

namespace BazaarPlusPlus.Game.RunLogging;

public sealed class RunLogSessionManager
{
    private readonly IRunLogStore _store;
    private readonly Func<DateTimeOffset> _utcNow;

    public RunLogSessionManager(IRunLogStore store, Func<DateTimeOffset>? utcNow = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public RunLogSessionState? ActiveSession { get; private set; }

    public bool HasActiveSession => ActiveSession != null;

    public RunLogSessionState? RestoreActiveSession()
    {
        if (ActiveSession != null)
            return ActiveSession;

        ActiveSession = _store.TryResumeActiveRun();
        return ActiveSession;
    }

    public RunLogSessionState EnsureActiveSession(RunLogCreateRequest request)
    {
        if (ActiveSession != null)
            return ActiveSession;

        ActiveSession = RestoreActiveSession() ?? _store.CreateRun(request);
        return ActiveSession;
    }

    public RunLogEvent? AppendEvent(RunLogEvent entry)
    {
        var session = ActiveSession ?? throw new InvalidOperationException("No active run session.");
        if (
            string.Equals(entry.Kind, "selection_seen", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(entry.SelectionFingerprint)
            && string.Equals(
                entry.SelectionFingerprint,
                session.LastSelectionFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            return null;
        }

        if (
            string.Equals(entry.Kind, "state_seen", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(entry.StateFingerprint)
            && string.Equals(
                entry.StateFingerprint,
                session.LastStateFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            return null;
        }

        entry.SchemaVersion = entry.SchemaVersion == 0 ? session.SchemaVersion : entry.SchemaVersion;
        entry.RunId = session.RunId;
        entry.Seq = session.LastSeq + 1;
        entry.Ts = entry.Ts == default ? _utcNow() : entry.Ts;

        _store.AppendEvent(session.RunId, entry);

        session.LastSeq = entry.Seq;
        session.LastSeenAtUtc = entry.Ts;
        session.Day = entry.Day ?? session.Day;
        session.Hour = entry.Hour ?? session.Hour;
        session.State = entry.State ?? session.State;
        session.CurrentEncounterId = entry.EncounterId ?? session.CurrentEncounterId;

        if (!string.IsNullOrWhiteSpace(entry.StateFingerprint))
            session.LastStateFingerprint = entry.StateFingerprint;

        if (!string.IsNullOrWhiteSpace(entry.SelectionFingerprint))
            session.LastSelectionFingerprint = entry.SelectionFingerprint;

        if (string.Equals(entry.Kind, "selection_seen", StringComparison.Ordinal))
            session.PendingSelectionSeq = entry.Seq;

        return entry;
    }

    public RunLogCheckpoint SaveCheckpoint()
    {
        var session = ActiveSession ?? throw new InvalidOperationException("No active run session.");
        var checkpoint = new RunLogCheckpoint
        {
            SchemaVersion = session.SchemaVersion,
            RunId = session.RunId,
            LastSeq = session.LastSeq,
            LastSeenAtUtc = session.LastSeenAtUtc,
            Day = session.Day,
            Hour = session.Hour,
            State = session.State,
            CurrentEncounterId = session.CurrentEncounterId,
            LastStateFingerprint = session.LastStateFingerprint,
            LastSelectionFingerprint = session.LastSelectionFingerprint,
            PendingSelectionSeq = session.PendingSelectionSeq,
            Completed = session.Completed,
        };

        _store.SaveCheckpoint(session.RunId, checkpoint);
        return checkpoint;
    }

    public void CompleteRun(RunLogCompletion completion)
    {
        var session = ActiveSession ?? throw new InvalidOperationException("No active run session.");
        completion.SchemaVersion = completion.SchemaVersion == 0
            ? session.SchemaVersion
            : completion.SchemaVersion;
        completion.RunId = session.RunId;
        if (completion.EndedAtUtc == default)
            completion.EndedAtUtc = _utcNow();

        _store.CompleteRun(session.RunId, completion);
        session.Completed = true;
        ActiveSession = null;
    }

    public void MarkRunAbandoned(RunLogAbandonment abandonment)
    {
        var session = ActiveSession ?? throw new InvalidOperationException("No active run session.");
        abandonment.SchemaVersion = abandonment.SchemaVersion == 0
            ? session.SchemaVersion
            : abandonment.SchemaVersion;
        abandonment.RunId = session.RunId;
        if (abandonment.EndedAtUtc == default)
            abandonment.EndedAtUtc = _utcNow();

        _store.MarkRunAbandoned(session.RunId, abandonment);
        session.Completed = true;
        ActiveSession = null;
    }
}
