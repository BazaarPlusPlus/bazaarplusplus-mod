#nullable enable
using System;
using System.Collections;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Screenshots.Persistence;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

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
    private RunScreenshotSqliteStore? _screenshotStore;
    private IDisposable? _runInitializedSubscription;
    private string? _bufferedRunId;
    private string? _bufferedHeroName;
    private bool? _lastContinueShouldBeInteractable;

    private void Awake()
    {
        _current = this;
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
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;
    }

    private void Update()
    {
        SyncEndOfRunContinueInteractivity();
    }

    private void OnRunStarted()
    {
        _gate.ResetForNewRun();
        ResetBufferedRunContext();
        RefreshBufferedRunContext();
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        if (!string.IsNullOrWhiteSpace(observed.RunId))
            _bufferedRunId = observed.RunId;

        RefreshBufferedRunContext();
    }

    public static bool TryConsumeContinuePassthrough()
    {
        return _current?.ConsumeContinuePassthrough() == true;
    }

    public static bool ShouldSuppressContinueWhileCaptureInFlight()
    {
        return _current?.GetShouldSuppressContinueWhileCaptureInFlight() == true;
    }

    private bool ConsumeContinuePassthrough()
    {
        return _gate.ConsumeContinuePassthrough();
    }

    private bool GetShouldSuppressContinueWhileCaptureInFlight()
    {
        return _gate.IsAttemptInFlight();
    }

    public static bool TryCaptureFirstContinue(
        EndOfRunScreenController controller,
        bool isInteractionBlocked
    )
    {
        return _current?.CaptureFirstContinue(controller, isInteractionBlocked) == true;
    }

    private bool CaptureFirstContinue(
        EndOfRunScreenController controller,
        bool isInteractionBlocked
    )
    {
        if (_screenshotService == null || !_gate.ShouldCaptureOnContinue(isInteractionBlocked))
            return false;

        StartCoroutine(CaptureAndContinue(controller));
        return true;
    }

    private IEnumerator CaptureAndContinue(EndOfRunScreenController controller)
    {
        ScreenshotCaptureResult? capture = null;
        using (BeginUiSuppression())
        {
            yield return new WaitForEndOfFrame();
            capture = _screenshotService?.CaptureCurrentFrame(
                new ScreenshotCaptureRequest
                {
                    RunId = ResolveRunId(),
                    HeroName = ResolveHeroName(),
                    CaptureSource = RunScreenshotCaptureSource.EndOfRunAuto,
                }
            );
        }
        var screenshotQueued = capture != null;
        if (screenshotQueued)
        {
            PersistCapture(capture, isPrimary: true);
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

    private void SyncEndOfRunContinueInteractivity()
    {
        var screenController = UnityEngine.Object.FindObjectOfType<EndOfRunScreenController>(
            includeInactive: true
        );
        if (screenController == null)
        {
            _lastContinueShouldBeInteractable = null;
            return;
        }

        if (
            !EndOfRunContinueStateEvaluator.TryShouldAllowContinue(
                screenController,
                _gate.IsAttemptInFlight(),
                out var shouldAllowContinue
            )
        )
        {
            _lastContinueShouldBeInteractable = null;
            return;
        }

        if (_lastContinueShouldBeInteractable == shouldAllowContinue)
            return;

        _lastContinueShouldBeInteractable = shouldAllowContinue;
        EndOfRunContinueButtonFeedback.SyncInteractivity(screenController, shouldAllowContinue);
    }
}
