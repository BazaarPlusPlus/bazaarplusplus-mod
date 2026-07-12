#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunScreenshot;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotController : MonoBehaviour
{
    private const float CaptureRetryCooldownSeconds = 1f;
    private const float CaptureAttemptTimeoutSeconds = 15f;
    private const float MetadataPersistenceTimeoutSeconds = 5f;
    private const float RevealFallbackTimeoutSeconds = 20f;
    private const float ControllerScanIntervalSeconds = 0.5f;
    private static EndOfRunScreenshotController? _current;
    private readonly EndOfRunScreenshotGate _gate = new();
    private readonly EndOfRunMouseBlocker _mouseBlocker = new();
    private ScreenshotService? _screenshotService;
    private RunScreenshotSqliteStore? _screenshotStore;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _captureSuppressionScope;
    private Coroutine? _captureCoroutine;
    private Task<ScreenshotCaptureResult?>? _activeCaptureTask;
    private string? _bufferedRunId;
    private string? _bufferedHeroName;
    private EndOfRunScreenController? _cachedEndOfRunScreenController;
    private float _nextControllerScanAtSeconds;
    private int _trackedEndOfRunControllerId;
    private int _captureGeneration;
    private IBppServices? _services;

    private void Awake()
    {
        _current = this;
    }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeCore();
    }

    private void InitializeCore()
    {
        var services = _services!;

        RefreshBufferedRunContext();

        var screenshotsDirectoryPath = services.Paths.ScreenshotsDirectoryPath;
        var runLogDatabasePath = services.Paths.RunLogDatabasePath;
        if (string.IsNullOrWhiteSpace(screenshotsDirectoryPath))
        {
            BppLog.Warn(
                "EndOfRunScreenshot",
                "Screenshot controller initialized without a screenshots directory."
            );
        }
        else
        {
            _screenshotService = new ScreenshotService(screenshotsDirectoryPath);
            if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
                _screenshotStore = new RunScreenshotSqliteStore(runLogDatabasePath);
        }

        // Catch-up subscribe if OnEnable fired before Initialize (normal case: AddComponent → Awake → OnEnable → Initialize).
        if (isActiveAndEnabled && _runInitializedSubscription == null)
        {
            _runInitializedSubscription = services.EventBus.Subscribe<RunInitializedObserved>(
                OnRunInitializedObserved
            );
        }
    }

    private void OnEnable()
    {
        _current = this;
        Events.RunStarted.AddListener(OnRunStarted, this);
        Events.EndOfRunScreenInitializing.AddListener(OnEndOfRunScreenInitializing, this);
        if (_services != null && _runInitializedSubscription == null)
        {
            _runInitializedSubscription = _services.EventBus.Subscribe<RunInitializedObserved>(
                OnRunInitializedObserved
            );
        }
    }

    private void OnDisable()
    {
        if (ReferenceEquals(_current, this))
            _current = null;

        Events.RunStarted.RemoveListener(OnRunStarted);
        Events.EndOfRunScreenInitializing.RemoveListener(OnEndOfRunScreenInitializing);
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
        ResetCaptureUiState(disarmGate: true);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;

        ResetCaptureUiState(disarmGate: true);
    }

    private void Update()
    {
        SyncEndOfRunCapture();
    }

    private void OnRunStarted()
    {
        ResetCaptureUiState(disarmGate: false);
        _gate.ResetForNewRun();
        ResetBufferedRunContext();
        RefreshBufferedRunContext();
    }

    private void OnEndOfRunScreenInitializing()
    {
        ResetCaptureUiState(disarmGate: true);
        _gate.ArmForEndOfRun();
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        if (!string.IsNullOrWhiteSpace(observed.RunId))
            _bufferedRunId = observed.RunId;

        RefreshBufferedRunContext();
    }

    public static bool ShouldBlockContinueUntilCapture(EndOfRunScreenController controller)
    {
        var current = _current;
        return current != null
            && current.isActiveAndEnabled
            && current.ShouldBlockContinueUntilCaptureInternal(controller);
    }

    public static void NotifySummaryRevealStarted(EndOfRunSummaryController summaryController)
    {
        var current = _current;
        if (current == null || !current.isActiveAndEnabled)
            return;

        current.MarkSummaryRevealStarted(summaryController);
    }

    private bool ShouldBlockContinueUntilCaptureInternal(EndOfRunScreenController controller)
    {
        if (!IsEndOfRunScreenshotEnabled() || _screenshotService == null)
            return false;

        EnsureCaptureArmed(controller);
        var readiness = GetCaptureReadiness(controller);
        return _gate.ShouldBlockContinue(
            readiness,
            isCaptureEnabled: true,
            Time.unscaledTime,
            RevealFallbackTimeoutSeconds
        );
    }

    private void SyncEndOfRunCapture()
    {
        if (!IsEndOfRunScreenshotEnabled() || _screenshotService == null)
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

        EnsureCaptureArmed(screenController);
        if (_gate.HasFinishedForCurrentRun())
        {
            _mouseBlocker.Detach();
            return;
        }

        var readiness = GetCaptureReadiness(screenController);
        if (
            _captureCoroutine == null
            && _gate.TryBeginAutomaticCapture(
                readiness,
                isCaptureEnabled: true,
                Time.unscaledTime,
                RevealFallbackTimeoutSeconds
            )
        )
        {
            _mouseBlocker.Attach(screenController);
            if (readiness == EndOfRunCaptureReadinessState.Ready)
            {
                BppLog.Debug(
                    "EndOfRunScreenshot",
                    $"CaptureState action=start frame={Time.frameCount} time={Time.unscaledTime:F3} trigger=reveal-complete readiness={readiness} runId={ResolveRunId() ?? "<null>"} hero={ResolveHeroName() ?? "<null>"}"
                );
            }
            else
            {
                BppLog.Warn(
                    "EndOfRunScreenshot",
                    $"CaptureState action=start frame={Time.frameCount} time={Time.unscaledTime:F3} trigger=timeout-fallback readiness={readiness} runId={ResolveRunId() ?? "<null>"} hero={ResolveHeroName() ?? "<null>"}; readiness detection did not recover before the bounded deadline."
                );
            }
            _captureCoroutine = StartCoroutine(
                CaptureEndOfRun(screenController, _captureGeneration)
            );
        }

        var shouldBlock = _gate.ShouldBlockContinue(
            readiness,
            isCaptureEnabled: true,
            Time.unscaledTime,
            RevealFallbackTimeoutSeconds
        );
        if (shouldBlock)
            _mouseBlocker.Attach(screenController);
        else
            _mouseBlocker.Detach();
    }

    private IEnumerator CaptureEndOfRun(
        EndOfRunScreenController screenController,
        int captureGeneration
    )
    {
        ScreenshotCaptureResult? capture = null;
        var attemptResolved = false;
        try
        {
            // FaceUp is set at the end of the native reveal task. Give its finally/layout
            // work a full player-loop turn before suppressing BPP chrome and reading pixels.
            yield return null;
            if (!IsCaptureContextCurrent(screenController, captureGeneration))
            {
                FailOpenIfCurrent(captureGeneration);
                attemptResolved = true;
                BppLog.Warn(
                    "EndOfRunScreenshot",
                    "Automatic capture was abandoned because the end-of-run screen changed before capture."
                );
                yield break;
            }

            _captureSuppressionScope = BeginUiSuppression();
            yield return new WaitForEndOfFrame();
            if (!IsCaptureContextCurrent(screenController, captureGeneration))
            {
                FailOpenIfCurrent(captureGeneration);
                attemptResolved = true;
                BppLog.Warn(
                    "EndOfRunScreenshot",
                    "Automatic capture was abandoned because the end-of-run screen changed during frame settling."
                );
                yield break;
            }

            Exception? captureFailure = null;
            Task<ScreenshotCaptureResult?>? captureTask = null;
            var attemptDeadline = Time.realtimeSinceStartup + CaptureAttemptTimeoutSeconds;
            try
            {
                captureTask = _screenshotService?.CaptureCurrentFrameAsync(
                    new ScreenshotCaptureRequest
                    {
                        RunId = ResolveRunId(),
                        HeroName = ResolveHeroName(),
                        CaptureSource = RunScreenshotCaptureSource.EndOfRunAuto,
                    }
                );
            }
            catch (Exception ex)
            {
                captureFailure = ex;
            }

            if (captureTask != null)
            {
                _activeCaptureTask = captureTask;
                while (!captureTask.IsCompleted)
                {
                    if (!IsCaptureContextCurrent(screenController, captureGeneration))
                    {
                        AbandonCaptureTask(captureTask);
                        FailOpenIfCurrent(captureGeneration);
                        attemptResolved = true;
                        BppLog.Warn(
                            "EndOfRunScreenshot",
                            "Automatic capture was abandoned because the end-of-run screen changed while the frame was being written."
                        );
                        yield break;
                    }

                    if (Time.realtimeSinceStartup >= attemptDeadline)
                    {
                        AbandonCaptureTask(captureTask);
                        FailOpenIfCurrent(captureGeneration);
                        attemptResolved = true;
                        BppLog.Error(
                            "EndOfRunScreenshot",
                            $"End-of-run screenshot capture exceeded {CaptureAttemptTimeoutSeconds:F0}s; releasing Continue without retrying the in-flight write."
                        );
                        yield break;
                    }

                    yield return null;
                }

                ReleaseCaptureTask(captureTask);
                try
                {
                    capture = captureTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    captureFailure = ex;
                }
            }

            if (captureFailure != null)
            {
                attemptResolved = true;
                HandleCaptureFailure(captureFailure);
            }
            else if (capture != null)
            {
                DisposeCaptureSuppressionScope();
                Exception? persistenceFailure = null;
                var persistenceTimedOut = false;
                Task? persistTask = null;
                try
                {
                    persistTask = PersistCaptureAsync(capture, isPrimary: true);
                }
                catch (Exception ex)
                {
                    persistenceFailure = ex;
                }

                if (persistTask != null)
                {
                    var persistenceDeadline =
                        Time.realtimeSinceStartup + MetadataPersistenceTimeoutSeconds;
                    while (
                        !persistTask.IsCompleted && Time.realtimeSinceStartup < persistenceDeadline
                    )
                    {
                        yield return null;
                    }

                    persistenceTimedOut = !persistTask.IsCompleted;
                    if (persistenceTimedOut)
                    {
                        ObserveLatePersistence(persistTask);
                    }
                    else
                    {
                        try
                        {
                            persistTask.GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            persistenceFailure = ex;
                        }
                    }
                }

                if (captureGeneration == _captureGeneration)
                    _gate.CompleteCaptureAttempt();
                attemptResolved = true;
                if (persistenceTimedOut)
                {
                    BppLog.Warn(
                        "EndOfRunScreenshot",
                        $"Screenshot metadata persistence exceeded {MetadataPersistenceTimeoutSeconds:F0}s; releasing Continue while the background save finishes."
                    );
                }
                else if (persistenceFailure != null)
                {
                    BppLog.Error(
                        "EndOfRunScreenshot",
                        "Failed to persist end-of-run screenshot metadata.",
                        persistenceFailure
                    );
                }
            }
            else
            {
                attemptResolved = true;
                HandleCaptureFailure(null);
            }
        }
        finally
        {
            AbandonActiveCaptureTask();
            _captureCoroutine = null;
            DisposeCaptureSuppressionScope();
            if (_gate.IsCaptureAttemptInFlight() && !attemptResolved)
                HandleCaptureFailure(null);
            _mouseBlocker.Detach();
        }
    }

    private void HandleCaptureFailure(Exception? failure)
    {
        var willRetry = _gate.AbortCaptureAttempt(Time.unscaledTime + CaptureRetryCooldownSeconds);
        var message = willRetry
            ? "End-of-run screenshot capture failed; one retry remains."
            : "End-of-run screenshot capture failed twice; releasing Continue without a screenshot.";
        if (failure == null)
            BppLog.Warn("EndOfRunScreenshot", message);
        else
            BppLog.Error("EndOfRunScreenshot", message, failure);
    }

    private void MarkSummaryRevealStarted(EndOfRunSummaryController summaryController)
    {
        var screenController = summaryController.StateMachine;
        if (screenController == null || !screenController.gameObject.activeInHierarchy)
            return;

        var controllerId = screenController.GetInstanceID();
        if (_trackedEndOfRunControllerId != 0 && _trackedEndOfRunControllerId != controllerId)
        {
            BppLog.Warn(
                "EndOfRunScreenshot",
                "Ignored a summary reveal signal from a stale end-of-run controller."
            );
            return;
        }

        TrackEndOfRunController(screenController);
        if (_gate.HasFinishedForCurrentRun() || _gate.HasSummaryRevealStarted())
            return;

        if (!_gate.IsArmed())
            _gate.ArmForEndOfRun();
        _gate.MarkSummaryRevealStarted();
    }

    private EndOfRunCaptureReadinessState GetCaptureReadiness(EndOfRunScreenController controller)
    {
        return EndOfRunCaptureReadinessDetector.GetState(
            controller,
            _gate.HasSummaryRevealStarted()
        );
    }

    private void FailOpenIfCurrent(int captureGeneration)
    {
        if (captureGeneration == _captureGeneration)
            _gate.FailOpen();
    }

    private static void ObserveAndDeleteLateCapture(Task<ScreenshotCaptureResult?> captureTask)
    {
        _ = captureTask.ContinueWith(
            task =>
            {
                try
                {
                    if (
                        task.Status == TaskStatus.RanToCompletion
                        && task.Result is { FilePath: { Length: > 0 } filePath }
                        && File.Exists(filePath)
                    )
                    {
                        File.Delete(filePath);
                        BppLog.Warn(
                            "EndOfRunScreenshot",
                            $"Deleted a screenshot that completed after its capture context expired: {filePath}"
                        );
                    }
                    else if (task.IsFaulted)
                    {
                        _ = task.Exception;
                    }
                }
                catch (Exception ex)
                {
                    BppLog.Warn(
                        "EndOfRunScreenshot",
                        $"Failed to clean up a late screenshot capture: {ex.Message}"
                    );
                }
            },
            TaskScheduler.Default
        );
    }

    private void AbandonCaptureTask(Task<ScreenshotCaptureResult?> captureTask)
    {
        ReleaseCaptureTask(captureTask);
        ObserveAndDeleteLateCapture(captureTask);
    }

    private void AbandonActiveCaptureTask()
    {
        var captureTask = _activeCaptureTask;
        if (captureTask != null)
            AbandonCaptureTask(captureTask);
    }

    private void ReleaseCaptureTask(Task<ScreenshotCaptureResult?> captureTask)
    {
        if (ReferenceEquals(_activeCaptureTask, captureTask))
            _activeCaptureTask = null;
    }

    private static void ObserveLatePersistence(Task persistTask)
    {
        _ = persistTask.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    BppLog.Error(
                        "EndOfRunScreenshot",
                        "Screenshot metadata persistence failed after its UI timeout.",
                        task.Exception!.GetBaseException()
                    );
                }
            },
            TaskScheduler.Default
        );
    }

    private Task PersistCaptureAsync(ScreenshotCaptureResult? capture, bool isPrimary = false)
    {
        if (capture == null || _screenshotStore == null)
            return Task.CompletedTask;

        var services = _services!;
        var probe = services.RunSnapshot;
        var basics = probe.TryGetRunBasics(out var basicsSnapshot) ? basicsSnapshot : null;
        var rank = probe.TryGetRankSnapshot(out var rankSnapshot) ? rankSnapshot : null;
        var position = probe.TryGetLeaderboardPosition(out var leaderboardPosition)
            ? leaderboardPosition
            : null;
        var record = RunScreenshotRecordMapper.CreateRecord(
            capture,
            basics,
            rank,
            position,
            isPrimary,
            services.GameBuild.Channel.ToString()
        );
        var screenshotStore = _screenshotStore;

        return Task.Run(() =>
        {
            try
            {
                screenshotStore.Save(record);
            }
            catch (Exception ex)
            {
                BppLog.Error("ScreenshotService", "Failed to persist screenshot metadata.", ex);
            }
        });
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

    private bool IsEndOfRunScreenshotEnabled()
    {
        return _services != null
            ? EndOfRunScreenshotSettingsPolicy.IsEnabledOrForced(_services.Config)
            : true;
    }

    private void ResetBufferedRunContext()
    {
        _bufferedRunId = null;
        _bufferedHeroName = null;
    }

    private void RefreshBufferedRunContext()
    {
        if (_services != null)
        {
            var liveRunId = _services.RunContext.CurrentServerRunId;
            if (!string.IsNullOrWhiteSpace(liveRunId))
                _bufferedRunId = liveRunId;
        }

        var liveHeroName = Data.Run?.Player?.Hero.ToString();
        if (!string.IsNullOrWhiteSpace(liveHeroName))
            _bufferedHeroName = liveHeroName;
    }

    private static IDisposable? BeginUiSuppression()
    {
        return BppUiChromeSuppression.Begin(BppUiChromeSuppressionMode.Screenshot);
    }

    private void DisposeCaptureSuppressionScope()
    {
        _captureSuppressionScope?.Dispose();
        _captureSuppressionScope = null;
    }

    private void ResetCaptureUiState(bool disarmGate)
    {
        _captureGeneration++;
        AbandonActiveCaptureTask();
        if (_captureCoroutine != null)
        {
            StopCoroutine(_captureCoroutine);
            _captureCoroutine = null;
        }

        DisposeCaptureSuppressionScope();
        _gate.CancelCaptureAttempt();
        if (disarmGate)
            _gate.Disarm();
        _mouseBlocker.Destroy();
        _cachedEndOfRunScreenController = null;
        _nextControllerScanAtSeconds = 0f;
        _trackedEndOfRunControllerId = 0;
    }

    private void EnsureCaptureArmed(EndOfRunScreenController controller)
    {
        TrackEndOfRunController(controller);
        if (_gate.IsArmed() || _gate.HasFinishedForCurrentRun())
            return;

        _gate.ArmForEndOfRun();
    }

    private bool IsCaptureContextCurrent(EndOfRunScreenController controller, int captureGeneration)
    {
        return captureGeneration == _captureGeneration
            && controller != null
            && controller.gameObject.activeInHierarchy
            && controller.GetInstanceID() == _trackedEndOfRunControllerId;
    }

    private EndOfRunScreenController? FindActiveEndOfRunScreenController()
    {
        var cached = _cachedEndOfRunScreenController;
        if (cached != null)
            return cached.gameObject.activeInHierarchy ? cached : null;

        if (AppState.CurrentState is not { } appState || !appState.IsEndOfRunState())
            return null;

        var now = Time.realtimeSinceStartup;
        if (now < _nextControllerScanAtSeconds)
            return null;
        _nextControllerScanAtSeconds = now + ControllerScanIntervalSeconds;

        _cachedEndOfRunScreenController = ScanForActiveEndOfRunScreenController();
        return _cachedEndOfRunScreenController;
    }

    private static EndOfRunScreenController? ScanForActiveEndOfRunScreenController()
    {
        var controllers = UnityEngine.Object.FindObjectsOfType<EndOfRunScreenController>(
            includeInactive: true
        );
        foreach (var controller in controllers)
        {
            if (controller != null && controller.gameObject.activeInHierarchy)
                return controller;
        }

        return null;
    }

    private void TrackEndOfRunController(EndOfRunScreenController controller)
    {
        var controllerId = controller.GetInstanceID();
        if (_trackedEndOfRunControllerId == controllerId)
            return;

        _trackedEndOfRunControllerId = controllerId;
    }
}
