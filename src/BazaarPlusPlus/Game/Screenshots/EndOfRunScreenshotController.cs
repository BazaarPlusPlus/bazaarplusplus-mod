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
    private Func<ScreenshotCaptureRequest, Task<ScreenshotCaptureResult?>>? _captureAsync;
    private Func<
        ScreenshotCaptureResult?,
        bool,
        Task<ScreenshotMetadataPersistenceOutcome>
    >? _persistAsync;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _captureSuppressionScope;
    private Coroutine? _captureCoroutine;
    private Task<ScreenshotCaptureResult?>? _activeCaptureTask;
    private string? _activeCaptureScreenshotId;
    private ScreenshotCaptureOperation? _captureOperation;
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
            ScreenshotCaptureDiagnostics.ReportInitializationFailed();
        }
        else
        {
            _screenshotService = new ScreenshotService(screenshotsDirectoryPath);
            _captureAsync = _screenshotService.CaptureCurrentFrameAsync;
            _persistAsync = PersistCaptureAsync;
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

    internal void ConfigureCaptureSeamsForTests(
        Func<ScreenshotCaptureRequest, Task<ScreenshotCaptureResult?>> captureAsync,
        Func<
            ScreenshotCaptureResult?,
            bool,
            Task<ScreenshotMetadataPersistenceOutcome>
        > persistAsync
    )
    {
        _captureAsync = captureAsync ?? throw new ArgumentNullException(nameof(captureAsync));
        _persistAsync = persistAsync ?? throw new ArgumentNullException(nameof(persistAsync));
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
        var readinessOutcome = GetCaptureReadiness(controller);
        EnsureCaptureOperation(readinessOutcome.State);
        var shouldBlock = _gate.ShouldBlockContinue(
            readinessOutcome.State,
            isCaptureEnabled: true,
            Time.unscaledTime,
            RevealFallbackTimeoutSeconds
        );
        CompleteReadinessFailureIfFinished(readinessOutcome);
        return shouldBlock;
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

        var readinessOutcome = GetCaptureReadiness(screenController);
        var readiness = readinessOutcome.State;
        var operation = EnsureCaptureOperation(readiness);
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
            if (operation == null)
                return;
            if (readiness != EndOfRunCaptureReadinessState.Ready)
            {
                operation.RecordDegradation(
                    readinessOutcome.ReasonCode ?? ScreenshotCaptureReasonCode.ReadinessDeadline,
                    readinessOutcome.Exception
                );
            }
            operation.BeginAttempt();
            _captureCoroutine = StartCoroutine(
                CaptureEndOfRun(screenController, _captureGeneration, operation)
            );
        }

        var shouldBlock = _gate.ShouldBlockContinue(
            readiness,
            isCaptureEnabled: true,
            Time.unscaledTime,
            RevealFallbackTimeoutSeconds
        );
        CompleteReadinessFailureIfFinished(readinessOutcome);
        if (shouldBlock)
            _mouseBlocker.Attach(screenController);
        else
            _mouseBlocker.Detach();
    }

    private IEnumerator CaptureEndOfRun(
        EndOfRunScreenController screenController,
        int captureGeneration,
        ScreenshotCaptureOperation operation
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
                operation.RecordAttemptFailure(
                    ScreenshotCaptureReasonCode.ContextExpired,
                    exception: null,
                    willRetry: false,
                    NowMilliseconds()
                );
                yield break;
            }

            _captureSuppressionScope = BeginUiSuppression();
            yield return new WaitForEndOfFrame();
            if (!IsCaptureContextCurrent(screenController, captureGeneration))
            {
                FailOpenIfCurrent(captureGeneration);
                attemptResolved = true;
                operation.RecordAttemptFailure(
                    ScreenshotCaptureReasonCode.ContextExpired,
                    exception: null,
                    willRetry: false,
                    NowMilliseconds()
                );
                yield break;
            }

            Exception? captureFailure = null;
            var captureFailureReason = ScreenshotCaptureReasonCode.CaptureReturnedNull;
            Task<ScreenshotCaptureResult?>? captureTask = null;
            var attemptDeadline = Time.realtimeSinceStartup + CaptureAttemptTimeoutSeconds;
            try
            {
                captureTask = _captureAsync?.Invoke(
                    new ScreenshotCaptureRequest
                    {
                        ScreenshotId = operation.ScreenshotId,
                        RunId = operation.RunId,
                        HeroName = ResolveHeroName(),
                        CaptureSource = RunScreenshotCaptureSource.EndOfRunAuto,
                    }
                );
            }
            catch (Exception ex)
            {
                captureFailure = ex;
                captureFailureReason = ScreenshotCaptureReasonCode.CaptureSynchronousException;
            }

            if (captureTask != null)
            {
                _activeCaptureTask = captureTask;
                _activeCaptureScreenshotId = operation.ScreenshotId;
                while (!captureTask.IsCompleted)
                {
                    if (!IsCaptureContextCurrent(screenController, captureGeneration))
                    {
                        AbandonCaptureTask(captureTask, operation.ScreenshotId);
                        FailOpenIfCurrent(captureGeneration);
                        attemptResolved = true;
                        operation.RecordAttemptFailure(
                            ScreenshotCaptureReasonCode.ContextExpired,
                            exception: null,
                            willRetry: false,
                            NowMilliseconds()
                        );
                        yield break;
                    }

                    if (Time.realtimeSinceStartup >= attemptDeadline)
                    {
                        AbandonCaptureTask(captureTask, operation.ScreenshotId);
                        FailOpenIfCurrent(captureGeneration);
                        attemptResolved = true;
                        operation.RecordAttemptFailure(
                            ScreenshotCaptureReasonCode.CaptureTimeout,
                            exception: null,
                            willRetry: false,
                            NowMilliseconds()
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
                    captureFailureReason = ScreenshotCaptureReasonCode.CaptureTaskFaulted;
                }
            }

            if (captureFailure != null)
            {
                attemptResolved = true;
                HandleCaptureFailure(operation, captureFailureReason, captureFailure);
            }
            else if (capture != null)
            {
                if (!IsUsableScreenshot(capture.FilePath))
                {
                    attemptResolved = true;
                    HandleCaptureFailure(
                        operation,
                        ScreenshotCaptureReasonCode.CaptureArtifactUnavailable,
                        failure: null
                    );
                    yield break;
                }

                operation.RecordVerifiedArtifact(capture.FilePath);
                DisposeCaptureSuppressionScope();
                var persistenceOutcome = ScreenshotMetadataPersistenceOutcome.Unavailable();
                Task<ScreenshotMetadataPersistenceOutcome>? persistTask = null;
                try
                {
                    persistTask = _persistAsync?.Invoke(capture, true);
                }
                catch (Exception ex)
                {
                    persistenceOutcome = ScreenshotMetadataPersistenceOutcome.Failed(ex);
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

                    if (!persistTask.IsCompleted)
                    {
                        ObserveLatePersistence(persistTask);
                        persistenceOutcome = ScreenshotMetadataPersistenceOutcome.TimedOut();
                    }
                    else
                    {
                        try
                        {
                            persistenceOutcome = persistTask.GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            persistenceOutcome = ScreenshotMetadataPersistenceOutcome.Failed(ex);
                        }
                    }
                }

                if (captureGeneration == _captureGeneration)
                    _gate.CompleteCaptureAttempt();
                attemptResolved = true;
                operation.TryCompleteArtifact(
                    artifactVerified: true,
                    capture.FilePath,
                    persistenceOutcome,
                    NowMilliseconds()
                );
            }
            else
            {
                attemptResolved = true;
                HandleCaptureFailure(
                    operation,
                    ScreenshotCaptureReasonCode.CaptureReturnedNull,
                    failure: null
                );
            }
        }
        finally
        {
            AbandonActiveCaptureTask();
            _captureCoroutine = null;
            DisposeCaptureSuppressionScope();
            if (_gate.IsCaptureAttemptInFlight() && !attemptResolved)
            {
                HandleCaptureFailure(
                    operation,
                    ScreenshotCaptureReasonCode.CaptureReturnedNull,
                    failure: null
                );
            }
            _mouseBlocker.Detach();
        }
    }

    private void HandleCaptureFailure(
        ScreenshotCaptureOperation operation,
        ScreenshotCaptureReasonCode reasonCode,
        Exception? failure
    )
    {
        var willRetry = _gate.AbortCaptureAttempt(Time.unscaledTime + CaptureRetryCooldownSeconds);
        operation.RecordAttemptFailure(reasonCode, failure, willRetry, NowMilliseconds());
    }

    private void MarkSummaryRevealStarted(EndOfRunSummaryController summaryController)
    {
        var screenController = summaryController.StateMachine;
        if (screenController == null || !screenController.gameObject.activeInHierarchy)
            return;

        var controllerId = screenController.GetInstanceID();
        if (_trackedEndOfRunControllerId != 0 && _trackedEndOfRunControllerId != controllerId)
            return;

        TrackEndOfRunController(screenController);
        if (_gate.HasFinishedForCurrentRun() || _gate.HasSummaryRevealStarted())
            return;

        if (!_gate.IsArmed())
            _gate.ArmForEndOfRun();
        _gate.MarkSummaryRevealStarted();
    }

    private EndOfRunCaptureReadinessOutcome GetCaptureReadiness(EndOfRunScreenController controller)
    {
        return EndOfRunCaptureReadinessDetector.GetOutcome(
            controller,
            _gate.HasSummaryRevealStarted()
        );
    }

    private ScreenshotCaptureOperation? EnsureCaptureOperation(
        EndOfRunCaptureReadinessState readiness
    )
    {
        if (_captureOperation != null)
            return _captureOperation;
        if (readiness == EndOfRunCaptureReadinessState.NotSummary)
            return null;

        _captureOperation = new ScreenshotCaptureOperation(
            Guid.NewGuid().ToString("N"),
            ResolveRunId(),
            RunScreenshotCaptureSource.EndOfRunAuto,
            NowMilliseconds()
        );
        return _captureOperation;
    }

    private void CompleteReadinessFailureIfFinished(
        EndOfRunCaptureReadinessOutcome readinessOutcome
    )
    {
        if (!_gate.HasFinishedForCurrentRun() || _gate.HasCapturedForCurrentRun())
            return;

        _captureOperation?.RecordAttemptFailure(
            readinessOutcome.ReasonCode ?? ScreenshotCaptureReasonCode.ReadinessDeadline,
            readinessOutcome.Exception,
            willRetry: false,
            NowMilliseconds()
        );
    }

    private void FailOpenIfCurrent(int captureGeneration)
    {
        if (captureGeneration == _captureGeneration)
            _gate.FailOpen();
    }

    private static void ObserveAndDeleteLateCapture(
        Task<ScreenshotCaptureResult?> captureTask,
        string? screenshotId
    )
    {
        _ = captureTask.ContinueWith(
            task =>
            {
                string? filePath = null;
                try
                {
                    if (
                        task.Status == TaskStatus.RanToCompletion
                        && task.Result is { FilePath: { Length: > 0 } completedFilePath }
                        && File.Exists(completedFilePath)
                    )
                    {
                        filePath = completedFilePath;
                        File.Delete(filePath);
                    }
                    else if (task.IsFaulted)
                    {
                        _ = task.Exception;
                    }
                }
                catch (Exception ex)
                {
                    ScreenshotCaptureDiagnostics.ReportCleanupFailed(
                        ScreenshotCaptureCleanupStage.LateFileDelete,
                        screenshotId,
                        filePath,
                        ex
                    );
                }
            },
            TaskScheduler.Default
        );
    }

    private void AbandonCaptureTask(
        Task<ScreenshotCaptureResult?> captureTask,
        string? screenshotId
    )
    {
        ReleaseCaptureTask(captureTask);
        ObserveAndDeleteLateCapture(captureTask, screenshotId);
    }

    private void AbandonActiveCaptureTask()
    {
        var captureTask = _activeCaptureTask;
        if (captureTask != null)
            AbandonCaptureTask(captureTask, _activeCaptureScreenshotId);
    }

    private void ReleaseCaptureTask(Task<ScreenshotCaptureResult?> captureTask)
    {
        if (ReferenceEquals(_activeCaptureTask, captureTask))
        {
            _activeCaptureTask = null;
            _activeCaptureScreenshotId = null;
        }
    }

    private static void ObserveLatePersistence(
        Task<ScreenshotMetadataPersistenceOutcome> persistTask
    )
    {
        _ = persistTask.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                    _ = task.Exception;
                else if (task.Status == TaskStatus.RanToCompletion)
                    _ = task.Result;
            },
            TaskScheduler.Default
        );
    }

    private Task<ScreenshotMetadataPersistenceOutcome> PersistCaptureAsync(
        ScreenshotCaptureResult? capture,
        bool isPrimary = false
    )
    {
        if (capture == null || _screenshotStore == null)
            return Task.FromResult(ScreenshotMetadataPersistenceOutcome.Unavailable());

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
                return ScreenshotMetadataPersistenceOutcome.Saved();
            }
            catch (Exception ex)
            {
                return ScreenshotMetadataPersistenceOutcome.Failed(ex);
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
        var operation = _captureOperation;
        if (operation != null)
        {
            operation.TryCompleteContextReset(NowMilliseconds());
            _captureOperation = null;
        }

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

    private static bool IsUsableScreenshot(string? filePath)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(filePath)
                && File.Exists(filePath)
                && new FileInfo(filePath).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static long NowMilliseconds() => (long)Math.Round(Time.realtimeSinceStartup * 1000d);

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
