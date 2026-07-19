#nullable enable
using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.OverlayPanels;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.UI.EndOfRun;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunCaptureDriver
    : MonoBehaviour,
        IEndOfRunCaptureSurface<EndOfRunScreenController>
{
    private const float ControllerScanIntervalSeconds = 0.5f;
    private const string ActiveControllerFieldName = "_activeController";
    private const string TransitionCountFieldName = "_transitionCount";
    private const string ContinueMethodName = "OnContinueClick";
    private static readonly BindingFlags NativeInstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? ActiveControllerField =
        typeof(EndOfRunScreenController).GetField(ActiveControllerFieldName, NativeInstanceFlags);
    private static readonly FieldInfo? TransitionCountField =
        typeof(EndOfRunScreenController).GetField(TransitionCountFieldName, NativeInstanceFlags);
    private static readonly MethodInfo? ContinueMethod = typeof(EndOfRunScreenController).GetMethod(
        ContinueMethodName,
        NativeInstanceFlags
    );
    private readonly EndOfRunMouseBlocker _mouseBlocker = new();
    private EndOfRunCaptureWorkflow? _workflow;
    private ScreenshotService? _screenshotService;
    private IDisposable? _runInitializedSubscription;
    private EndOfRunScreenController? _cachedScreen;
    private float _nextControllerScanAtSeconds;
    private IBppServices? _services;

    public bool IsAvailable => isActiveAndEnabled && _screenshotService != null;

    internal void Initialize(EndOfRunCaptureWorkflow workflow, IBppServices services)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(services.Paths.ScreenshotsDirectoryPath))
            ScreenshotCaptureDiagnostics.ReportInitializationFailed();
        else
            _screenshotService = new ScreenshotService(services.Paths.ScreenshotsDirectoryPath);

        _workflow.AttachDriver(this);
        SubscribeRunInitializedIfReady();
    }

    private void OnEnable()
    {
        Events.RunStarted.AddListener(OnRunStarted, this);
        Events.EndOfRunScreenInitializing.AddListener(OnEndOfRunScreenInitializing, this);
        if (_workflow != null)
            _workflow.AttachDriver(this);
        SubscribeRunInitializedIfReady();
    }

    private void OnDisable()
    {
        Events.RunStarted.RemoveListener(OnRunStarted);
        Events.EndOfRunScreenInitializing.RemoveListener(OnEndOfRunScreenInitializing);
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
        _workflow?.DetachDriver(this);
        _mouseBlocker.Destroy();
        _cachedScreen = null;
        _nextControllerScanAtSeconds = 0f;
    }

    private void OnDestroy()
    {
        _workflow?.DetachDriver(this);
        _mouseBlocker.Destroy();
    }

    private void Update() => _workflow?.OnFrame();

    public EndOfRunScreenController? FindActiveScreen()
    {
        var cached = _cachedScreen;
        if (cached != null)
        {
            if (cached.gameObject.activeInHierarchy)
                return cached;
            _cachedScreen = null;
        }
        if (AppState.CurrentState is not { } appState || !appState.IsEndOfRunState())
            return null;

        var now = Time.realtimeSinceStartup;
        if (now < _nextControllerScanAtSeconds)
            return null;
        _nextControllerScanAtSeconds = now + ControllerScanIntervalSeconds;

        foreach (
            var controller in UnityEngine.Object.FindObjectsOfType<EndOfRunScreenController>(
                includeInactive: true
            )
        )
        {
            if (controller != null && controller.gameObject.activeInHierarchy)
            {
                _cachedScreen = controller;
                return controller;
            }
        }
        return null;
    }

    public bool IsScreenActive(EndOfRunScreenController screen) =>
        screen != null && screen.gameObject.activeInHierarchy;

    public int GetScreenId(EndOfRunScreenController screen) => screen.GetInstanceID();

    public EndOfRunCaptureReadinessOutcome GetReadiness(
        EndOfRunScreenController screen,
        bool hasRevealStarted
    ) => EndOfRunCaptureWorkflow.ReadReadiness(screen, hasRevealStarted);

    public bool TryGetContinueTarget(
        EndOfRunScreenController screen,
        out EndOfRunContinueTarget target
    )
    {
        target = default;
        if (
            GetActiveControllerSelection(screen) != ActiveControllerSelection.UniqueExpected
            || !TryGetActiveSummary(screen, out var summary)
        )
            return false;

        target = new EndOfRunContinueTarget(screen.GetInstanceID(), summary.GetInstanceID());
        return true;
    }

    public bool TryGetContinueTargetAmbiguity(EndOfRunScreenController screen, out bool ambiguous)
    {
        var selection = GetActiveControllerSelection(screen);
        ambiguous = selection == ActiveControllerSelection.Competing;
        return selection
            is ActiveControllerSelection.UniqueExpected
                or ActiveControllerSelection.Competing;
    }

    public bool IsContinueTargetCurrent(
        EndOfRunScreenController screen,
        EndOfRunContinueTarget target
    ) => EndOfRunNativeContinueVerifier.IsTargetCurrent(ReadNativeContinueState(screen), target);

    public IEndOfRunCaptureAttempt BeginCapture(
        EndOfRunScreenController screen,
        ScreenshotCaptureRequest request
    )
    {
        if (_screenshotService == null)
            throw new InvalidOperationException("Screenshot capture service is unavailable.");
        return new UnityCaptureAttempt(this, request, _screenshotService.CaptureCurrentFrameAsync);
    }

    public EndOfRunContinueResumeOutcome ResumeContinue(
        EndOfRunScreenController screen,
        EndOfRunContinueTarget target
    )
    {
        if (
            GetActiveControllerSelection(screen) != ActiveControllerSelection.UniqueExpected
            || !IsContinueTargetCurrent(screen, target)
            || ContinueMethod == null
        )
            return new EndOfRunContinueResumeOutcome(EndOfRunContinueResumeStatus.ContextStale);

        try
        {
            ContinueMethod.Invoke(screen, parameters: null);
        }
        catch (TargetInvocationException ex)
        {
            return new EndOfRunContinueResumeOutcome(
                EndOfRunContinueResumeStatus.Failed,
                ex.InnerException ?? ex
            );
        }
        catch (Exception ex)
        {
            return new EndOfRunContinueResumeOutcome(EndOfRunContinueResumeStatus.Failed, ex);
        }

        return EndOfRunNativeContinueVerifier.HasAdvanced(ReadNativeContinueState(screen), target)
            ? new EndOfRunContinueResumeOutcome(EndOfRunContinueResumeStatus.Advanced)
            : new EndOfRunContinueResumeOutcome(EndOfRunContinueResumeStatus.NativeNoOp);
    }

    public void SetContinueBlocked(EndOfRunScreenController? screen, bool blocked)
    {
        if (blocked && screen != null)
            _mouseBlocker.Attach(screen);
        else
            _mouseBlocker.Detach();
    }

    private void OnRunStarted()
    {
        _cachedScreen = null;
        _nextControllerScanAtSeconds = 0f;
        _workflow?.OnRunStarted();
    }

    private void OnEndOfRunScreenInitializing()
    {
        _cachedScreen = null;
        _nextControllerScanAtSeconds = 0f;
        _workflow?.OnEndOfRunInitializing();
    }

    private void SubscribeRunInitializedIfReady()
    {
        if (!isActiveAndEnabled || _services == null || _runInitializedSubscription != null)
            return;
        _runInitializedSubscription = _services.EventBus.Subscribe<RunInitializedObserved>(
            observed => _workflow?.ObserveRunInitialized(observed)
        );
    }

    private static bool TryGetActiveSummary(
        EndOfRunScreenController? screen,
        out EndOfRunSummaryController summary
    )
    {
        summary = null!;
        if (!TryGetActiveController(screen, out var activeController))
            return false;
        if (activeController is not EndOfRunSummaryController activeSummary)
            return false;
        summary = activeSummary;
        return true;
    }

    private static bool TryGetActiveController(
        EndOfRunScreenController? screen,
        out EndOfRunBaseController controller
    )
    {
        controller = null!;
        if (screen == null || ActiveControllerField == null)
            return false;

        try
        {
            if (
                ActiveControllerField.GetValue(screen)
                is not EndOfRunBaseController activeController
            )
                return false;
            controller = activeController;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetTransitionCount(
        EndOfRunScreenController? screen,
        out int transitionCount
    )
    {
        transitionCount = 0;
        if (screen == null || TransitionCountField == null)
            return false;

        try
        {
            transitionCount = (int?)TransitionCountField.GetValue(screen) ?? 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static EndOfRunNativeContinueState ReadNativeContinueState(
        EndOfRunScreenController? screen
    )
    {
        var screenId = 0;
        var screenStateAvailable = false;
        var screenActive = false;
        try
        {
            if (!ReferenceEquals(screen, null) && screen == null)
            {
                screenStateAvailable = true;
            }
            else if (screen != null)
            {
                screenId = screen.GetInstanceID();
                screenActive = screen.gameObject.activeInHierarchy;
                screenStateAvailable = true;
            }
        }
        catch
        {
            screenStateAvailable = false;
            screenActive = false;
        }

        var endOfRunStateAvailable = false;
        var isEndOfRun = false;
        try
        {
            if (AppState.CurrentState is { } appState)
            {
                endOfRunStateAvailable = true;
                isEndOfRun = appState.IsEndOfRunState();
            }
        }
        catch
        {
            endOfRunStateAvailable = false;
        }

        var sceneTransitionAvailable = TryGetSceneTransition(out var sceneTransitioning);
        var transitionCountAvailable = TryGetTransitionCount(screen, out var transitionCount);
        var activeControllerAvailable = TryGetActiveController(screen, out var activeController);
        var activeControllerId = 0;
        var activeControllerActive = false;
        if (activeControllerAvailable)
        {
            try
            {
                activeControllerId = activeController.GetInstanceID();
                activeControllerActive = activeController.gameObject.activeInHierarchy;
            }
            catch
            {
                activeControllerAvailable = false;
            }
        }

        return new EndOfRunNativeContinueState(
            screenId,
            screenStateAvailable,
            screenActive,
            endOfRunStateAvailable,
            isEndOfRun,
            sceneTransitionAvailable,
            sceneTransitioning,
            transitionCountAvailable,
            transitionCount,
            activeControllerAvailable,
            activeControllerId,
            activeControllerActive
        );
    }

    private static ActiveControllerSelection GetActiveControllerSelection(
        EndOfRunScreenController expected
    )
    {
        try
        {
            var activeCount = 0;
            foreach (
                var controller in UnityEngine.Object.FindObjectsOfType<EndOfRunScreenController>(
                    includeInactive: true
                )
            )
            {
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    continue;
                if (!ReferenceEquals(controller, expected))
                    return ActiveControllerSelection.Competing;
                activeCount++;
            }

            return activeCount == 1
                ? ActiveControllerSelection.UniqueExpected
                : ActiveControllerSelection.ExpectedNotActive;
        }
        catch
        {
            return ActiveControllerSelection.DetectionFailed;
        }
    }

    private static bool TryGetSceneTransition(out bool transitioning)
    {
        transitioning = false;
        try
        {
            var sceneLoader = Services.Get<SceneLoader>();
            if (sceneLoader == null)
                return false;
            transitioning = sceneLoader.IsTransitioning;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private enum ActiveControllerSelection
    {
        UniqueExpected,
        Competing,
        ExpectedNotActive,
        DetectionFailed,
    }

    private sealed class UnityCaptureAttempt : IEndOfRunCaptureAttempt
    {
        private readonly object _gate = new();
        private readonly EndOfRunCaptureDriver _driver;
        private readonly ScreenshotCaptureRequest _request;
        private readonly Func<
            ScreenshotCaptureRequest,
            Task<ScreenshotCaptureResult?>
        > _captureAsync;
        private readonly TaskCompletionSource<EndOfRunCaptureAttemptOutcome> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private IDisposable? _suppression;
        private Task<ScreenshotCaptureResult?>? _captureTask;
        private bool _canceled;
        private bool _hasCaptureStarted;

        internal UnityCaptureAttempt(
            EndOfRunCaptureDriver driver,
            ScreenshotCaptureRequest request,
            Func<ScreenshotCaptureRequest, Task<ScreenshotCaptureResult?>> captureAsync
        )
        {
            _driver = driver;
            _request = request;
            _captureAsync = captureAsync;
            _driver.StartCoroutine(Run());
        }

        public Task<EndOfRunCaptureAttemptOutcome> Completion => _completion.Task;

        public bool HasCaptureStarted
        {
            get
            {
                lock (_gate)
                    return _hasCaptureStarted;
            }
        }

        public void Cancel()
        {
            Task<ScreenshotCaptureResult?>? captureTask;
            lock (_gate)
            {
                if (_canceled)
                    return;
                _canceled = true;
                captureTask = _captureTask;
            }

            DisposeSuppression();
            if (captureTask != null)
                CompleteFromTaskWhenReady(captureTask);
        }

        public void RestoreUi() => DisposeSuppression();

        private IEnumerator Run()
        {
            yield return null;
            if (IsCanceled())
            {
                CompleteCanceled();
                yield break;
            }

            _suppression = BppUiChromeSuppression.Begin(BppUiChromeSuppressionMode.Screenshot);
            yield return new WaitForEndOfFrame();
            if (IsCanceled())
            {
                DisposeSuppression();
                CompleteCanceled();
                yield break;
            }

            Task<ScreenshotCaptureResult?>? task;
            try
            {
                lock (_gate)
                    _hasCaptureStarted = true;
                task = _captureAsync(_request);
            }
            catch (Exception ex)
            {
                DisposeSuppression();
                _completion.TrySetResult(
                    EndOfRunCaptureAttemptOutcome.Failed(
                        ScreenshotCaptureReasonCode.CaptureSynchronousException,
                        ex
                    )
                );
                yield break;
            }

            if (task == null)
            {
                DisposeSuppression();
                _completion.TrySetResult(
                    EndOfRunCaptureAttemptOutcome.Failed(
                        ScreenshotCaptureReasonCode.CaptureReturnedNull
                    )
                );
                yield break;
            }

            lock (_gate)
                _captureTask = task;
            if (IsCanceled())
            {
                DisposeSuppression();
                CompleteFromTaskWhenReady(task);
                yield break;
            }

            while (!task.IsCompleted && !IsCanceled())
                yield return null;

            if (IsCanceled())
            {
                DisposeSuppression();
                CompleteFromTaskWhenReady(task);
                yield break;
            }

            CompleteFromTask(task);
        }

        private void CompleteFromTaskWhenReady(Task<ScreenshotCaptureResult?> task)
        {
            _ = task.ContinueWith(CompleteFromTask, TaskScheduler.Default);
        }

        private void CompleteFromTask(Task<ScreenshotCaptureResult?> task)
        {
            try
            {
                if (task.IsCanceled)
                {
                    CompleteCanceled();
                    return;
                }
                if (task.IsFaulted)
                {
                    _completion.TrySetResult(
                        EndOfRunCaptureAttemptOutcome.Failed(
                            ScreenshotCaptureReasonCode.CaptureTaskFaulted,
                            task.Exception?.GetBaseException()
                        )
                    );
                    return;
                }

                var capture = task.GetAwaiter().GetResult();
                _completion.TrySetResult(
                    capture == null
                        ? EndOfRunCaptureAttemptOutcome.Failed(
                            ScreenshotCaptureReasonCode.CaptureReturnedNull
                        )
                        : EndOfRunCaptureAttemptOutcome.Succeeded(capture)
                );
            }
            catch (Exception ex)
            {
                _completion.TrySetResult(
                    EndOfRunCaptureAttemptOutcome.Failed(
                        ScreenshotCaptureReasonCode.CaptureTaskFaulted,
                        ex
                    )
                );
            }
        }

        private void CompleteCanceled() =>
            _completion.TrySetResult(
                EndOfRunCaptureAttemptOutcome.Failed(ScreenshotCaptureReasonCode.ContextExpired)
            );

        private bool IsCanceled()
        {
            lock (_gate)
                return _canceled;
        }

        private void DisposeSuppression()
        {
            IDisposable? suppression;
            lock (_gate)
            {
                suppression = _suppression;
                _suppression = null;
            }
            suppression?.Dispose();
        }
    }
}
