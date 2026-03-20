#nullable enable
using System;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLoggingController : MonoBehaviour
{
    public static RunLoggingController? Instance { get; private set; }

    private IRunLogStore? _store;
    private RunLogSessionManager? _sessionManager;
    private RunLogCaptureService? _captureService;
    private RunLogInferenceService? _inferenceService;
    private RunLoggingControllerCore? _core;
    private bool _wasInRunLastTick;

    public RunLogSessionManager? SessionManager => _sessionManager;

    public RunLogCaptureService? CaptureService => _captureService;

    public RunLogInferenceService? InferenceService => _inferenceService;

    private void Awake()
    {
        Instance = this;
        var runLogDatabasePath = ModState.RunLogDatabasePath
            ?? throw new InvalidOperationException("Run log database path is not initialized.");
        _store = new SqliteRunLogStore(runLogDatabasePath);
        _sessionManager = new RunLogSessionManager(_store);
        _sessionManager.RestoreActiveSession();
        _captureService = new RunLogCaptureService();
        _inferenceService = new RunLogInferenceService();
        _core = new RunLoggingControllerCore(_sessionManager, _captureService);
        BppLog.Info(
            "RunLoggingController",
            $"Initialized run logging database: {ModState.RunLogDatabasePath}"
        );
    }

    public RunLogSessionState EnsureActiveSession(RunLogCreateRequest request)
    {
        return RequireCore().EnsureRunStarted(request);
    }

    public RunLogEvent? AppendEvent(RunLogEvent entry)
    {
        return RequireSessionManager().AppendEvent(entry);
    }

    public RunLogCheckpoint SaveCheckpoint()
    {
        return RequireSessionManager().SaveCheckpoint();
    }

    public void CompleteRun(RunLogCompletion completion)
    {
        RequireSessionManager().CompleteRun(completion);
    }

    public void MarkRunAbandoned(RunLogAbandonment abandonment)
    {
        RequireSessionManager().MarkRunAbandoned(abandonment);
    }

    public void PollRunState()
    {
        var inRun = ModState.IsInGameRun;
        var completionAttempted = false;
        var completionSucceeded = false;
        try
        {
            if (inRun)
            {
                var session = EnsureActiveRunFromGame();
                if (session != null)
                {
                    if (GameDataReader.TryBuildRunLogRunProgressInput(out var progressInput))
                        RequireCore().AcceptRunProgress(progressInput);

                    if (GameDataReader.TryBuildRunLogStateSnapshot(out var stateInput))
                        RequireCore().AcceptStateSnapshot(stateInput);
                }
            }
            else if (_wasInRunLastTick && _sessionManager?.HasActiveSession == true)
            {
                completionAttempted = true;
                RequireCore().CompleteRun(GameDataReader.BuildRunLogCompletion("run_state_exit"));
                completionSucceeded = true;
            }
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingController", $"PollRunState failed: {ex}");
        }
        finally
        {
            if (inRun)
            {
                _wasInRunLastTick = true;
            }
            else if (
                !completionAttempted
                || completionSucceeded
                || _sessionManager?.HasActiveSession != true
            )
            {
                _wasInRunLastTick = false;
            }
        }
    }

    public void CaptureSelectionFromCurrentState()
    {
        try
        {
            if (!ModState.IsInGameRun)
                return;

            if (EnsureActiveRunFromGame() == null)
                return;

            if (GameDataReader.TryBuildRunLogSelectionSnapshot(out var selectionInput))
                RequireCore().AcceptSelectionSnapshot(selectionInput);
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingController", $"CaptureSelectionFromCurrentState failed: {ex}");
        }
    }

    public RunLogEvent AcceptRunProgress(RunLogRunProgressInput input)
    {
        return RequireCore().AcceptRunProgress(input);
    }

    public RunLogEvent AcceptStateSnapshot(RunLogStateSnapshotInput input)
    {
        return RequireCore().AcceptStateSnapshot(input);
    }

    public RunLogEvent? AcceptSelectionSnapshot(RunLogSelectionSnapshotInput input)
    {
        return RequireCore().AcceptSelectionSnapshot(input);
    }

    public RunLogEvent? CapturePvpBattle(PvpBattleManifest manifest)
    {
        try
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            if (
                !string.Equals(manifest.CombatKind, "PVPCombat", StringComparison.Ordinal)
                || !ModState.IsInGameRun
            )
            {
                return null;
            }

            if (EnsureActiveRunFromGame() == null)
                return null;

            return RequireCore().AcceptCombatReplay(
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
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingController", $"CapturePvpBattle failed: {ex}");
            return null;
        }
    }

    private RunLogSessionState? EnsureActiveRunFromGame()
    {
        if (!GameDataReader.TryCreateRunLogCreateRequest(out var request))
            return null;

        return RequireCore().EnsureRunStarted(request);
    }

    private RunLogSessionManager RequireSessionManager()
    {
        return _sessionManager
            ?? throw new InvalidOperationException("Run logging controller is not initialized.");
    }

    private RunLoggingControllerCore RequireCore()
    {
        return _core
            ?? throw new InvalidOperationException(
                "Run logging controller core is not initialized."
            );
    }
}

internal sealed class RunLoggingControllerCore
{
    private readonly RunLogSessionManager _sessionManager;
    private readonly RunLogCaptureService _captureService;
    private string? _startedEventRunId;

    public RunLoggingControllerCore(
        RunLogSessionManager sessionManager,
        RunLogCaptureService captureService
    )
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
    }

    public RunLogSessionState EnsureRunStarted(RunLogCreateRequest request)
    {
        var session = _sessionManager.EnsureActiveSession(request);
        if (string.Equals(_startedEventRunId, session.RunId, StringComparison.Ordinal))
            return session;

        var eventKind = session.LastSeq > 0 ? "run_resumed" : "run_started";
        _sessionManager.AppendEvent(
            new RunLogEvent
            {
                Kind = eventKind,
                Day = session.Day ?? request.Day,
                Hour = session.Hour ?? request.Hour,
                Hero = request.Hero,
                GameMode = request.GameMode,
            }
        );
        _sessionManager.SaveCheckpoint();
        _startedEventRunId = session.RunId;
        return session;
    }

    public RunLogEvent AcceptRunProgress(RunLogRunProgressInput input)
    {
        var runEvent =
            _sessionManager.AppendEvent(_captureService.BuildRunProgressEvent(input))
            ?? throw new InvalidOperationException(
                "Run progress event was unexpectedly suppressed."
            );
        _sessionManager.SaveCheckpoint();
        return runEvent;
    }

    public RunLogEvent AcceptStateSnapshot(RunLogStateSnapshotInput input)
    {
        var stateEvent =
            _sessionManager.AppendEvent(_captureService.BuildStateSeenEvent(input))
            ?? throw new InvalidOperationException("State event was unexpectedly suppressed.");
        _sessionManager.SaveCheckpoint();
        return stateEvent;
    }

    public RunLogEvent? AcceptSelectionSnapshot(RunLogSelectionSnapshotInput input)
    {
        var selectionEvent = _sessionManager.AppendEvent(
            _captureService.BuildSelectionSeenEvent(input)
        );
        if (selectionEvent != null)
            _sessionManager.SaveCheckpoint();

        return selectionEvent;
    }

    public RunLogEvent AcceptCombatReplay(RunLogPvpBattleInput input)
    {
        var combatEvent =
            _sessionManager.AppendEvent(_captureService.BuildPvpBattleRecordedEvent(input))
            ?? throw new InvalidOperationException("Combat replay event was unexpectedly suppressed.");
        _sessionManager.SaveCheckpoint();
        return combatEvent;
    }

    public void CompleteRun(RunLogCompletion completion)
    {
        _sessionManager.CompleteRun(completion);
        _startedEventRunId = null;
    }
}
