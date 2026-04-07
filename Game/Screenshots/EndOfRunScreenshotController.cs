#nullable enable
using System;
using System.Collections;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotController : MonoBehaviour
{
    private static readonly System.Reflection.MethodInfo ContinueClickMethod = AccessTools.Method(
        typeof(EndOfRunScreenController),
        "OnContinueClick"
    )!;
    private static EndOfRunScreenshotController? _current;
    private readonly EndOfRunScreenshotGate _gate = new();
    private ScreenshotService? _screenshotService;
    private IDisposable? _runInitializedSubscription;
    private string? _currentRunId;

    private void Awake()
    {
        _current = this;
        _currentRunId = BppRuntimeHost.RunContext.CurrentServerRunId;

        var screenshotsDirectoryPath = BppRuntimeHost.Paths.ScreenshotsDirectoryPath;
        if (string.IsNullOrWhiteSpace(screenshotsDirectoryPath))
        {
            BppLog.Warn(
                "EndOfRunScreenshot",
                "Screenshot controller initialized without a screenshots directory."
            );
            return;
        }

        _screenshotService = new ScreenshotService(screenshotsDirectoryPath);
    }

    private void OnEnable()
    {
        Events.RunStarted.AddListener(OnRunStarted, this);
        _runInitializedSubscription = BppRuntimeHost.EventBus.Subscribe<RunInitializedObserved>(
            OnRunInitializedObserved
        );
    }

    private void OnDisable()
    {
        Events.RunStarted.RemoveListener(OnRunStarted);
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;
    }

    private void OnRunStarted()
    {
        _gate.ResetForNewRun();
        _currentRunId = null;
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        _currentRunId = string.IsNullOrWhiteSpace(observed.RunId) ? null : observed.RunId;
    }

    public static bool TryCaptureFirstContinue(
        EndOfRunScreenController controller,
        bool isInteractionBlocked
    )
    {
        return _current?.CaptureFirstContinue(controller, isInteractionBlocked) == true;
    }

    public static bool TryConsumeContinuePassthrough()
    {
        return _current?.ConsumeContinuePassthrough() == true;
    }

    private bool ConsumeContinuePassthrough()
    {
        return _gate.ConsumeContinuePassthrough();
    }

    private bool CaptureFirstContinue(EndOfRunScreenController controller, bool isInteractionBlocked)
    {
        if (_screenshotService == null || !_gate.ShouldCaptureOnContinue(isInteractionBlocked))
            return false;

        StartCoroutine(CaptureAndContinue(controller));
        return true;
    }

    private IEnumerator CaptureAndContinue(EndOfRunScreenController controller)
    {
        yield return new WaitForEndOfFrame();
        var screenshotQueued = _screenshotService?.CaptureCurrentFrame(ResolveRunId()) != null;
        if (screenshotQueued)
        {
            _gate.MarkAttemptCompleted();
        }
        else
        {
            _gate.MarkAttemptAborted();
            BppLog.Warn(
                "EndOfRunScreenshot",
                "Screenshot attempt aborted before it could be queued."
            );
        }

        yield return null;

        _gate.AllowNextContinuePassthrough();
        ContinueClickMethod.Invoke(controller, []);
    }

    private string? ResolveRunId()
    {
        return !string.IsNullOrWhiteSpace(_currentRunId)
            ? _currentRunId
            : BppRuntimeHost.RunContext.CurrentServerRunId;
    }
}
