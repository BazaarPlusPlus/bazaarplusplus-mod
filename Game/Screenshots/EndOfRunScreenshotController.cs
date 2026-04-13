#nullable enable
using System;
using System.Collections;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Screenshots.Persistence;
using BazaarPlusPlus.Game.Settings;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotController : MonoBehaviour
{
    private const float CaptureRetryCooldownSeconds = 1f;
    private readonly EndOfRunScreenshotGate _gate = new();
    private readonly EndOfRunMouseBlocker _mouseBlocker = new();
    private ScreenshotService? _screenshotService;
    private RunScreenshotSqliteStore? _screenshotStore;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _captureSuppressionScope;
    private Coroutine? _captureCoroutine;
    private string? _bufferedRunId;
    private string? _bufferedHeroName;

    private void Awake()
    {
        RefreshBufferedRunContext();

        var screenshotsDirectoryPath = BppRuntimeHost.Paths.ScreenshotsDirectoryPath;
        var runLogDatabasePath = BppRuntimeHost.Paths.RunLogDatabasePath;
        if (string.IsNullOrWhiteSpace(screenshotsDirectoryPath))
        {
            BppLog.Warn(
                "EndOfRunScreenshot",
                "Screenshot controller initialized without a screenshots directory."
            );
            return;
        }

        _screenshotService = new ScreenshotService(screenshotsDirectoryPath);
        if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
            _screenshotStore = new RunScreenshotSqliteStore(runLogDatabasePath);
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
        ResetCaptureUiState();
    }

    private void OnDestroy()
    {
        ResetCaptureUiState();
    }

    private void Update()
    {
        SyncEndOfRunMouseBlocker();
    }

    private void OnRunStarted()
    {
        _gate.ResetForNewRun();
        ResetCaptureUiState();
        ResetBufferedRunContext();
        RefreshBufferedRunContext();
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        if (!string.IsNullOrWhiteSpace(observed.RunId))
            _bufferedRunId = observed.RunId;

        RefreshBufferedRunContext();
    }

    private IEnumerator CaptureEndOfRun()
    {
        ScreenshotCaptureResult? capture = null;
        try
        {
            _captureSuppressionScope = BeginUiSuppression();
            yield return new WaitForEndOfFrame();

            try
            {
                capture = _screenshotService?.CaptureCurrentFrame(
                    new ScreenshotCaptureRequest
                    {
                        RunId = ResolveRunId(),
                        HeroName = ResolveHeroName(),
                        CaptureSource = RunScreenshotCaptureSource.EndOfRunAuto,
                    }
                );
                if (capture != null)
                {
                    PersistCapture(capture, isPrimary: true);
                    _gate.CompleteCaptureAttempt();
                }
                else
                {
                    _gate.AbortCaptureAttempt(Time.unscaledTime + CaptureRetryCooldownSeconds);
                    BppLog.Warn(
                        "EndOfRunScreenshot",
                        "Screenshot attempt aborted before it could be queued."
                    );
                }
            }
            catch (Exception ex)
            {
                _gate.AbortCaptureAttempt(Time.unscaledTime + CaptureRetryCooldownSeconds);
                BppLog.Error("EndOfRunScreenshot", "End-of-run screenshot capture failed.", ex);
            }
        }
        finally
        {
            _captureCoroutine = null;
            DisposeCaptureSuppressionScope();
            if (_gate.IsCaptureAttemptInFlight())
                _gate.AbortCaptureAttempt(Time.unscaledTime + CaptureRetryCooldownSeconds);
            _mouseBlocker.Detach();
        }
    }

    private void PersistCapture(ScreenshotCaptureResult? capture, bool isPrimary = false)
    {
        if (capture == null || _screenshotStore == null)
            return;

        try
        {
            _screenshotStore.Save(RunScreenshotMetadataReader.CreateRecord(capture, isPrimary));
        }
        catch (Exception ex)
        {
            BppLog.Error("ScreenshotService", "Failed to persist screenshot metadata.", ex);
        }
    }

    private string? ResolveRunId()
    {
        RefreshBufferedRunContext();
        return _bufferedRunId;
    }

    private string? ResolveHeroName()
    {
        RefreshBufferedRunContext();
        return _bufferedHeroName;
    }

    private void ResetBufferedRunContext()
    {
        _bufferedRunId = null;
        _bufferedHeroName = null;
    }

    private void RefreshBufferedRunContext()
    {
        var liveRunId = BppRuntimeHost.RunContext.CurrentServerRunId;
        if (!string.IsNullOrWhiteSpace(liveRunId))
            _bufferedRunId = liveRunId;

        var liveHeroName = Data.Run?.Player?.Hero.ToString();
        if (!string.IsNullOrWhiteSpace(liveHeroName))
            _bufferedHeroName = liveHeroName;
    }

    private static IDisposable? BeginUiSuppression()
    {
        return ScreenshotUiSuppressionScope.Begin(
            BppSettingsDockController.BeginScreenshotSuppression,
            CombatStatusBarFeature.BeginScreenshotSuppression
        );
    }

    private void DisposeCaptureSuppressionScope()
    {
        _captureSuppressionScope?.Dispose();
        _captureSuppressionScope = null;
    }

    private void ResetCaptureUiState()
    {
        if (_captureCoroutine != null)
        {
            StopCoroutine(_captureCoroutine);
            _captureCoroutine = null;
        }

        DisposeCaptureSuppressionScope();
        _gate.CancelCaptureAttempt();
        _mouseBlocker.Detach();
    }

    private static EndOfRunScreenController? FindActiveEndOfRunScreenController()
    {
        var controllers = UnityEngine.Object.FindObjectsOfType<EndOfRunScreenController>(
            includeInactive: true
        );
        foreach (var controller in controllers)
        {
            if (controller == null)
                continue;

            if (controller.gameObject.activeInHierarchy)
                return controller;
        }

        return null;
    }

    private void SyncEndOfRunMouseBlocker()
    {
        if (_screenshotService == null)
        {
            _mouseBlocker.Detach();
            return;
        }

        var screenController = FindActiveEndOfRunScreenController();
        if (screenController == null)
        {
            _mouseBlocker.Detach();
            return;
        }

        if (_gate.IsCaptureAttemptInFlight())
        {
            _mouseBlocker.Attach(screenController);
            return;
        }

        if (
            !EndOfRunContinueStateEvaluator.TryShouldAllowContinue(
                screenController,
                suppressWhileCaptureInFlight: false,
                out var shouldAllowContinue
            )
        )
        {
            _mouseBlocker.Detach();
            return;
        }

        if (!shouldAllowContinue)
        {
            _mouseBlocker.Attach(screenController);
            return;
        }

        if (!_gate.TryBeginCapture(isInteractionBlocked: false, Time.unscaledTime))
        {
            _mouseBlocker.Detach();
            return;
        }

        _mouseBlocker.Attach(screenController);
        _captureCoroutine = StartCoroutine(CaptureEndOfRun());
    }
}
