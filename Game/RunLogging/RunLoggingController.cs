#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLoggingController : MonoBehaviour
{
    private JsonRunLogStore? _store;
    private RunLogSessionManager? _sessionManager;
    private RunLogCaptureService? _captureService;
    private RunLogInferenceService? _inferenceService;

    public RunLogSessionManager? SessionManager => _sessionManager;

    public RunLogCaptureService? CaptureService => _captureService;

    public RunLogInferenceService? InferenceService => _inferenceService;

    private void Awake()
    {
        _store = new JsonRunLogStore(ModState.RunLogRootPath);
        _sessionManager = new RunLogSessionManager(_store);
        _sessionManager.RestoreActiveSession();
        _captureService = new RunLogCaptureService();
        _inferenceService = new RunLogInferenceService();
        BppLog.Info("RunLoggingController", $"Initialized run logging root: {ModState.RunLogRootPath}");
    }

    public RunLogSessionState EnsureActiveSession(RunLogCreateRequest request)
    {
        return RequireSessionManager().EnsureActiveSession(request);
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

    private RunLogSessionManager RequireSessionManager()
    {
        return _sessionManager
            ?? throw new InvalidOperationException("Run logging controller is not initialized.");
    }
}
