#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLoggingModule
{
    private static readonly TimeSpan ReplayPersistenceCompletionGracePeriod = TimeSpan.FromSeconds(
        2
    );

    private readonly IBppEventBus _eventBus;
    private readonly IRunContext _runContext;
    private readonly RunLogSessionManager _sessionManager;
    private readonly RunLoggingControllerCore _core;
    private readonly Func<bool> _hasPendingReplayPersistence;
    private readonly Func<RunLogSessionState?> _ensureActiveRunFromGame;
    private readonly Action<string, string> _attachBattleToRun;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _pvpBattleSubscription;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _replayPersistenceDrainedSubscription;
    private RunLogCompletion? _deferredRunCompletion;
    private string? _deferredRunCompletionRunId;
    private DateTime? _deferredRunCompletionDeadlineUtc;
    private string? _pendingInterruptedRunId;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<string, RunLogCompletion> _buildRunLogCompletion;
    private readonly Func<string, RunLogAbandonment> _buildRunLogAbandonment;

    public RunLoggingModule(
        IBppEventBus eventBus,
        IRunContext runContext,
        RunLogSessionManager sessionManager,
        RunLoggingControllerCore core,
        Func<bool> hasPendingReplayPersistence,
        Func<RunLogSessionState?> ensureActiveRunFromGame,
        Func<DateTime> utcNow,
        Func<string, RunLogCompletion> buildRunLogCompletion,
        Func<string, RunLogAbandonment> buildRunLogAbandonment
    )
        : this(
            eventBus,
            runContext,
            sessionManager,
            core,
            hasPendingReplayPersistence,
            ensureActiveRunFromGame,
            static (_, _) => { },
            utcNow,
            buildRunLogCompletion,
            buildRunLogAbandonment
        ) { }

    public RunLoggingModule(
        IBppEventBus eventBus,
        IRunContext runContext,
        RunLogSessionManager sessionManager,
        RunLoggingControllerCore core,
        Func<bool> hasPendingReplayPersistence,
        Func<RunLogSessionState?> ensureActiveRunFromGame,
        Action<string, string> attachBattleToRun,
        Func<DateTime> utcNow,
        Func<string, RunLogCompletion> buildRunLogCompletion,
        Func<string, RunLogAbandonment> buildRunLogAbandonment
    )
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _hasPendingReplayPersistence =
            hasPendingReplayPersistence
            ?? throw new ArgumentNullException(nameof(hasPendingReplayPersistence));
        _ensureActiveRunFromGame =
            ensureActiveRunFromGame
            ?? throw new ArgumentNullException(nameof(ensureActiveRunFromGame));
        _attachBattleToRun =
            attachBattleToRun ?? throw new ArgumentNullException(nameof(attachBattleToRun));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _buildRunLogCompletion =
            buildRunLogCompletion ?? throw new ArgumentNullException(nameof(buildRunLogCompletion));
        _buildRunLogAbandonment =
            buildRunLogAbandonment
            ?? throw new ArgumentNullException(nameof(buildRunLogAbandonment));
    }

    public void Start()
    {
        _runLifecycleSubscription = _eventBus.Subscribe<RunLifecycleChanged>(OnRunLifecycleChanged);
        _pvpBattleSubscription = _eventBus.Subscribe<PvpBattleRecorded>(OnPvpBattleRecorded);
        _runInitializedSubscription = _eventBus.Subscribe<RunInitializedObserved>(
            OnRunInitializedObserved
        );
        _replayPersistenceDrainedSubscription = _eventBus.Subscribe<CombatReplayPersistenceDrained>(
            OnCombatReplayPersistenceDrained
        );
    }

    public void Stop()
    {
        if (_deferredRunCompletion != null && _sessionManager.HasActiveSession)
        {
            var runId = _deferredRunCompletionRunId ?? _sessionManager.ActiveSession?.RunId;
            try
            {
                TryCompleteDeferredRunExit(forceCompletion: true);
            }
            catch (Exception ex)
            {
                BppLog.ErrorEvent(
                    RunLoggingLogEvents.CompletionFailed,
                    ex,
                    RunLoggingLogEvents.RunId.Bind(runId),
                    RunLoggingLogEvents.FailureReasonCode.Bind(
                        RunLoggingReasonCode.TeardownFinalizationException
                    )
                );
            }
        }

        ClearDeferredRunCompletion();
        ClearPendingInterruptedRun();
        _replayPersistenceDrainedSubscription?.Dispose();
        _replayPersistenceDrainedSubscription = null;
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
        _pvpBattleSubscription?.Dispose();
        _pvpBattleSubscription = null;
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        try
        {
            if (!_runContext.IsInGameRun)
                return;

            HandleRunActivation(observed.RunId);
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                RunLoggingLogEvents.ActivationFailed,
                ex,
                RunLoggingLogEvents.RunId.Bind(observed.RunId),
                RunLoggingLogEvents.FailureReasonCode.Bind(
                    RunLoggingReasonCode.RunActivationException
                )
            );
        }
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        string? runId = null;
        var transition = RunLoggingTransition.Unknown;
        try
        {
            runId = _sessionManager.ActiveSession?.RunId ?? _runContext.CurrentServerRunId;
            transition = ToLogTransition(change);
            if (change.IsInGameRun)
            {
                HandleRunActivation(_runContext.CurrentServerRunId);
                return;
            }

            if (!_sessionManager.HasActiveSession)
                return;

            var activeSession = _sessionManager.ActiveSession;
            if (activeSession == null)
                return;

            if (IsInterruptedTransition(change))
            {
                _pendingInterruptedRunId = activeSession.RunId;
                return;
            }

            if (!IsCompletedTransition(change))
                return;

            ClearPendingInterruptedRun();
            _deferredRunCompletionRunId = activeSession.RunId;
            _deferredRunCompletion = _buildRunLogCompletion("run_state_exit");
            TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                RunLoggingLogEvents.TransitionFailed,
                ex,
                RunLoggingLogEvents.RunId.Bind(runId),
                RunLoggingLogEvents.Transition.Bind(transition),
                RunLoggingLogEvents.TransitionFailureReasonCode.Bind(
                    RunLoggingReasonCode.RunTransitionException
                )
            );
        }
    }

    private void OnPvpBattleRecorded(PvpBattleRecorded recorded)
    {
        PvpBattleManifest? manifest = null;
        try
        {
            manifest = recorded.Manifest;
            var inRun = _runContext.IsInGameRun;
            if (
                manifest == null
                || !string.Equals(manifest.CombatKind, "PVPCombat", StringComparison.Ordinal)
            )
            {
                return;
            }

            var session = TryResolveReplayTargetSession(manifest, inRun);
            if (session == null)
                return;

            manifest.RunId = session.RunId;
            if (!string.IsNullOrWhiteSpace(manifest.BattleId))
                _attachBattleToRun(manifest.BattleId, session.RunId);

            _core.AcceptCombatReplay(
                new RunLogPvpBattleInput
                {
                    Day = manifest.Day,
                    Hour = manifest.Hour,
                    EncounterId = manifest.EncounterId,
                    CombatKind = manifest.CombatKind,
                    BattleId = manifest.BattleId,
                    OpponentName = manifest.Participants.OpponentName,
                }
            );

            if (!inRun)
                TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            EmitBattleCaptureFailure(manifest, RunLoggingReasonCode.BattleCaptureException, ex);
        }
    }

    private void OnCombatReplayPersistenceDrained(CombatReplayPersistenceDrained drained)
    {
        string? runId = null;
        try
        {
            runId = _deferredRunCompletionRunId ?? _sessionManager.ActiveSession?.RunId;
            if (!_runContext.IsInGameRun)
                TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                RunLoggingLogEvents.ReplayDrainHandlingFailed,
                ex,
                RunLoggingLogEvents.RunId.Bind(runId),
                RunLoggingLogEvents.FailureReasonCode.Bind(
                    RunLoggingReasonCode.ReplayDrainHandlingException
                )
            );
        }
    }

    private void HandleRunActivation(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return;

        var activeSession = _sessionManager.ActiveSession;
        if (
            !string.IsNullOrWhiteSpace(_pendingInterruptedRunId)
            && activeSession != null
            && string.Equals(
                activeSession.RunId,
                _pendingInterruptedRunId,
                StringComparison.Ordinal
            )
        )
        {
            if (string.Equals(runId, _pendingInterruptedRunId, StringComparison.Ordinal))
            {
                ClearPendingInterruptedRun();
            }
            else
            {
                _sessionManager.MarkRunAbandoned(_buildRunLogAbandonment("run_interrupted"));
                ClearPendingInterruptedRun();
            }
        }

        CancelDeferredRunExitIfRunResumed();
        _ensureActiveRunFromGame();
    }

    private RunLogSessionState? TryResolveReplayTargetSession(
        PvpBattleManifest manifest,
        bool inRun
    )
    {
        if (inRun)
        {
            var session = _ensureActiveRunFromGame();
            if (session == null)
                return null;

            if (
                !string.IsNullOrWhiteSpace(manifest.RunId)
                && !string.Equals(session.RunId, manifest.RunId, StringComparison.Ordinal)
            )
            {
                EmitBattleCaptureFailure(manifest, RunLoggingReasonCode.InRunMismatch);
                return null;
            }

            return session;
        }

        var deferredSession = _sessionManager.ActiveSession;
        if (deferredSession == null)
            return null;

        if (string.IsNullOrWhiteSpace(manifest.RunId))
        {
            EmitBattleCaptureFailure(
                manifest,
                RunLoggingReasonCode.ManifestRunUnavailable,
                fallbackRunId: deferredSession.RunId
            );
            return null;
        }

        if (!string.Equals(deferredSession.RunId, manifest.RunId, StringComparison.Ordinal))
        {
            EmitBattleCaptureFailure(manifest, RunLoggingReasonCode.DeferredRunMismatch);
            return null;
        }

        return deferredSession;
    }

    private bool TryCompleteDeferredRunExit(bool forceCompletion = false)
    {
        if (_deferredRunCompletion == null)
            return false;

        var activeSession = _sessionManager.ActiveSession;
        if (
            activeSession == null
            || !string.Equals(
                activeSession.RunId,
                _deferredRunCompletionRunId,
                StringComparison.Ordinal
            )
        )
        {
            ClearDeferredRunCompletion();
            return false;
        }

        var now = _utcNow();
        RunLoggingReasonCode? degradationReason = null;
        if (!forceCompletion && _hasPendingReplayPersistence())
        {
            var deadline =
                _deferredRunCompletionDeadlineUtc ?? (now + ReplayPersistenceCompletionGracePeriod);
            _deferredRunCompletionDeadlineUtc = deadline;
            if (now < deadline)
                return false;

            degradationReason = RunLoggingReasonCode.ReplayDrainTimeout;
        }
        else if (forceCompletion && _hasPendingReplayPersistence())
        {
            degradationReason = RunLoggingReasonCode.ShutdownForced;
        }

        var completedRunId = activeSession.RunId;
        _core.CompleteRun(_deferredRunCompletion);
        ClearDeferredRunCompletion();
        if (degradationReason.HasValue)
        {
            BppLog.WarnEvent(
                RunLoggingLogEvents.CompletionDegraded,
                RunLoggingLogEvents.RunId.Bind(completedRunId),
                RunLoggingLogEvents.CompletionDegradedReasonCode.Bind(degradationReason.Value),
                RunLoggingLogEvents.GraceMilliseconds.Bind(
                    (long)ReplayPersistenceCompletionGracePeriod.TotalMilliseconds
                )
            );
        }
        return true;
    }

    private void EmitBattleCaptureFailure(
        PvpBattleManifest? manifest,
        RunLoggingReasonCode reasonCode,
        Exception? exception = null,
        string? fallbackRunId = null
    )
    {
        var runId = !string.IsNullOrWhiteSpace(manifest?.RunId)
            ? manifest.RunId
            : fallbackRunId
                ?? _sessionManager.ActiveSession?.RunId
                ?? _deferredRunCompletionRunId
                ?? _runContext.CurrentServerRunId;
        var fields = new[]
        {
            RunLoggingLogEvents.RunId.Bind(runId),
            RunLoggingLogEvents.BattleId.Bind(manifest?.BattleId),
            RunLoggingLogEvents.BattleFailureReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.ErrorEvent(RunLoggingLogEvents.BattleCaptureFailed, fields);
        else
            BppLog.ErrorEvent(RunLoggingLogEvents.BattleCaptureFailed, exception, fields);
    }

    private static RunLoggingTransition ToLogTransition(RunLifecycleChanged change)
    {
        if (change.IsInGameRun)
            return RunLoggingTransition.RunEntered;
        if (IsCompletedTransition(change))
            return RunLoggingTransition.RunEnded;
        if (IsInterruptedTransition(change))
            return RunLoggingTransition.RunInterrupted;
        if (string.Equals(change.Reason, "Live run-state reconciliation", StringComparison.Ordinal))
            return RunLoggingTransition.StateReconciled;
        return RunLoggingTransition.Unknown;
    }

    private void CancelDeferredRunExitIfRunResumed()
    {
        if (
            _deferredRunCompletion == null
            || string.IsNullOrWhiteSpace(_deferredRunCompletionRunId)
        )
            return;

        var currentRunId = _runContext.CurrentServerRunId;
        var activeSession = _sessionManager.ActiveSession;
        if (
            activeSession != null
            && string.Equals(
                activeSession.RunId,
                _deferredRunCompletionRunId,
                StringComparison.Ordinal
            )
            && string.Equals(currentRunId, _deferredRunCompletionRunId, StringComparison.Ordinal)
        )
        {
            ClearDeferredRunCompletion();
        }
    }

    private void ClearDeferredRunCompletion()
    {
        _deferredRunCompletion = null;
        _deferredRunCompletionRunId = null;
        _deferredRunCompletionDeadlineUtc = null;
    }

    private void ClearPendingInterruptedRun()
    {
        _pendingInterruptedRunId = null;
    }

    private static bool IsCompletedTransition(RunLifecycleChanged change)
    {
        return string.Equals(change.Reason, RunLifecycleReasons.RunEnded, StringComparison.Ordinal);
    }

    private static bool IsInterruptedTransition(RunLifecycleChanged change)
    {
        return string.Equals(
            change.Reason,
            RunLifecycleReasons.RunInterrupted,
            StringComparison.Ordinal
        );
    }
}
