#nullable enable
using System;
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
        _store = new SqliteRunLogStore(ModState.RunLogDatabasePath);
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
            RequireCore().CompleteRun(GameDataReader.BuildRunLogCompletion("run_state_exit"));
        }

        _wasInRunLastTick = inRun;
    }

    public void CaptureSelectionFromCurrentState()
    {
        if (!ModState.IsInGameRun)
            return;

        if (EnsureActiveRunFromGame() == null)
            return;

        if (GameDataReader.TryBuildRunLogSelectionSnapshot(out var selectionInput))
            RequireCore().AcceptSelectionSnapshot(selectionInput);
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

    private RunLogSessionState? EnsureActiveRunFromGame()
    {
        if (_sessionManager?.HasActiveSession == true)
            return _sessionManager.ActiveSession;

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
            ?? throw new InvalidOperationException("Run logging controller core is not initialized.");
    }
}

internal sealed class RunLoggingControllerCore
{
    private readonly RunLogSessionManager _sessionManager;
    private readonly RunLogCaptureService _captureService;
    private bool _runStartedEventWritten;

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
        if (_runStartedEventWritten)
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
        _runStartedEventWritten = true;
        return session;
    }

    public RunLogEvent AcceptRunProgress(RunLogRunProgressInput input)
    {
        var runEvent = _sessionManager.AppendEvent(_captureService.BuildRunProgressEvent(input))
            ?? throw new InvalidOperationException("Run progress event was unexpectedly suppressed.");
        _sessionManager.SaveCheckpoint();
        return runEvent;
    }

    public RunLogEvent AcceptStateSnapshot(RunLogStateSnapshotInput input)
    {
        var stateEvent = _sessionManager.AppendEvent(_captureService.BuildStateSeenEvent(input))
            ?? throw new InvalidOperationException("State event was unexpectedly suppressed.");
        _sessionManager.SaveCheckpoint();
        return stateEvent;
    }

    public RunLogEvent? AcceptSelectionSnapshot(RunLogSelectionSnapshotInput input)
    {
        var selectionEvent = _sessionManager.AppendEvent(_captureService.BuildSelectionSeenEvent(input));
        if (selectionEvent != null)
            _sessionManager.SaveCheckpoint();

        return selectionEvent;
    }

    public void CompleteRun(RunLogCompletion completion)
    {
        _sessionManager.CompleteRun(completion);
        _runStartedEventWritten = false;
    }
}
