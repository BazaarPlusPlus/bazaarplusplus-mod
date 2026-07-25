#nullable enable
using System.Collections;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.CombatReplay.ReportAssets;
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using BazaarPlusPlus.Game.CombatReplay.Reports;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.GameInterop.Files;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayRuntime : MonoBehaviour
{
    private const float CurrentReplayRecapPostRollSeconds = 3f;
    private const int StartupReportRecoveryBatchSize = 2;

    private IBppServices? _services;
    private RunLifecycleModule? _runLifecycle;
    private CombatReplayCaptureService? _captureService;
    private CombatReplayLoader? _loader;
    private CombatReplayController? _controller;
    private ReplayPersistenceOrchestrator? _persistence;
    private ReplayPlaybackPublisher? _playbackPublisher;
    private OpponentPortraitController? _portraitController;
    private ReplayPlaybackLogOperation? _activePlaybackOperation;
    private PendingReplayMenuReturn? _pendingMenuReturn;
    private ReplayPlaybackReasonCode _startupInterruptionReason;
    private Exception? _startupInterruptionException;
    private Func<CombatReplayVideoRecorder?>? _videoRecorder;
    private readonly CurrentReplayRecordingState _currentRecording = new();
    private PvpBattleManifest? _currentRecordingManifest;
    private PvpBattleManifest? _pendingReportAssetManifest;
    private string? _pendingReportAssetRecordingId;
    private IDisposable? _recordingStartedSubscription;
    private IDisposable? _recordingCompletedSubscription;
    private Coroutine? _pendingCurrentReplayStart;
    private Coroutine? _pendingCurrentReplayRecapPostRoll;
    private PostCombatReportNativeCardAssetExporter? _reportCardAssetExporter;
    private PostCombatReportNativeSpriteMaterializer? _reportNativeSpriteMaterializer;
    private CombatReplayReportPublicationCoordinator? _reportPublication;
    private readonly Dictionary<string, Task<bool>> _pendingReportDraftCaptures = new(
        StringComparer.Ordinal
    );
    private Coroutine? _pendingReportAssetPreparation;
    private TaskCompletionSource<
        IReadOnlyList<PostCombatReportAssetFile>
    >? _reportAssetPreparationCompletion;
    private Coroutine? _pendingReportRecovery;
    private IReadOnlyList<PostCombatReportAssetFile> _pendingPreparedReportAssets =
        Array.Empty<PostCombatReportAssetFile>();
    private Action? _invokeCurrentRecordingRecap;
    private bool _destroying;

    private bool _returnToMenuAfterReplay;
    private bool _bootstrappedReplayActive;

    // Joint progress of a saved-replay playback session. "Start in progress" and "playback
    // session active" overlap by design (the session is live for the whole start), so the
    // states encode their combinations rather than one-hot flags:
    //   Idle                -> no session, no start in flight
    //   StartInProgress     -> StartReplayAsync running, playback session live
    //   SavedPlaybackActive -> start finished, playback session live until ReplayState exits
    //   StartFailureCleanup -> playback session cleared but StartReplayAsync is still
    //                          unwinding (failure publish + bootstrap rollback)
    private enum SavedReplayProgress
    {
        Idle,
        StartInProgress,
        SavedPlaybackActive,
        StartFailureCleanup,
    }

    private SavedReplayProgress _savedReplayProgress;

    // Latched while a replay exit is in flight but ReplayState has not actually been left yet
    // (the bootstrapped exit path keeps CurrentState == ReplayState for the whole async
    // menu-return). Guards against a second Exit(): after the first exit cleared the
    // bootstrapped flags, a duplicate Exit() would run the original ReplayState.Exit() body
    // (whose own _exitRequested was never set on the rerouted path) and dispatch the dead
    // replay's despawn GameSim into the live state machine mid transition.
    //
    // The suppression is TIME-BOUNDED: ReturnToMainMenu awaits a network call internally and
    // can silently fail, leaving the game parked in ReplayState forever. Past the window, a
    // fresh Exit() (native click or continue endpoint) is allowed through again as the escape
    // hatch — running the original Exit body is the lesser evil versus a permanently dead
    // continue button.
    private const float ReplayExitSuppressionWindowSeconds = 15f;
    private bool _replayExitInProgress;
    private float _replayExitRequestedAtRealtime;

    private bool IsReplayExitSuppressionActive =>
        _replayExitInProgress
        && Time.realtimeSinceStartup - _replayExitRequestedAtRealtime
            < ReplayExitSuppressionWindowSeconds;

    private void LatchReplayExitInProgress()
    {
        _replayExitInProgress = true;
        _replayExitRequestedAtRealtime = Time.realtimeSinceStartup;
    }

    public static CombatReplayRuntime? Instance { get; private set; }

    // Sourced from the playback session (BeginSession sets it for both the local-saved and the
    // imported-ghost path); the controller only learns battle ids on the local-saved path.
    public string? ActiveBattleId => _playbackPublisher?.ActiveSessionBattleId;

    public bool IsReplayPlaybackActive =>
        IsSavedReplayPlaybackActive || AppState.CurrentState is ReplayState;

    public bool IsSavedReplayPlaybackActive =>
        _savedReplayProgress
            is SavedReplayProgress.StartInProgress
                or SavedReplayProgress.SavedPlaybackActive;

    public bool IsReplayStartInProgress =>
        _savedReplayProgress
            is SavedReplayProgress.StartInProgress
                or SavedReplayProgress.StartFailureCleanup;

    public bool HasPendingPersistence => _persistence?.HasPendingPersistence == true;

    private void Awake()
    {
        Instance = this;
    }

    public void Initialize(
        IBppServices services,
        RunLifecycleModule runLifecycle,
        IPvpBattleCatalog battleCatalog,
        Func<CombatReplayVideoRecorder?> videoRecorder
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _runLifecycle = runLifecycle ?? throw new ArgumentNullException(nameof(runLifecycle));
        _videoRecorder = videoRecorder ?? throw new ArgumentNullException(nameof(videoRecorder));

        _persistence = new ReplayPersistenceOrchestrator(
            _services,
            battleCatalog,
            OnReplayPersistenceCompleted
        );
        _playbackPublisher = new ReplayPlaybackPublisher(_services);
        _portraitController = new OpponentPortraitController(Destroy);
        _captureService = new CombatReplayCaptureService();
        _loader = new CombatReplayLoader();
        _controller = new CombatReplayController(
            _persistence.Catalog,
            _persistence.PayloadStore,
            _loader
        );
        var reportDataRoot = Path.Combine(BepInEx.Paths.GameRootPath, "BazaarPlusPlusV4");
        var reportAssetRoot = Path.Combine(reportDataRoot, "report-assets");
        _reportCardAssetExporter = new PostCombatReportNativeCardAssetExporter(
            reportAssetRoot,
            _services.GameBuild.RawVersion
        );
        _reportNativeSpriteMaterializer = new PostCombatReportNativeSpriteMaterializer(
            reportAssetRoot,
            _services.GameBuild.RawVersion
        );
        _reportPublication = new CombatReplayReportPublicationCoordinator(reportDataRoot);

        Events.StateChanged.AddListener(OnStateChanged, this);
        Events.ReplayStarted.AddListener(OnNativeReplayStarted, this);
        Events.ReplayEnded.AddListener(OnNativeReplayEnded, this);
        _recordingStartedSubscription =
            _services.EventBus.Subscribe<CombatReplayVideoRecordingStarted>(
                OnVideoRecordingStarted
            );
        _recordingCompletedSubscription =
            _services.EventBus.Subscribe<CombatReplayVideoRecordingCompleted>(
                OnVideoRecordingCompleted
            );

        RecoverIncompleteStaticReports(battleCatalog, reportDataRoot);
    }

    private void RecoverIncompleteStaticReports(
        IPvpBattleCatalog battleCatalog,
        string reportDataRoot
    )
    {
        var services = _services;
        var persistence = _persistence;
        var loader = _loader;
        var cardAssetExporter = _reportCardAssetExporter;
        var nativeSpriteMaterializer = _reportNativeSpriteMaterializer;
        var publication = _reportPublication;
        var runLogDatabasePath = services?.Paths.RunLogDatabasePath;
        var videoRootDirectoryPath = services?.Paths.CombatReplayVideoDirectoryPath;
        if (
            services == null
            || persistence == null
            || loader == null
            || cardAssetExporter == null
            || publication == null
            || string.IsNullOrWhiteSpace(runLogDatabasePath)
            || string.IsNullOrWhiteSpace(videoRootDirectoryPath)
        )
        {
            return;
        }

        try
        {
            var metadataStore = new CombatReplayVideoMetadataStore(runLogDatabasePath);
            var publisher = new CombatReplayReportRecoveryPublisher(
                battleCatalog,
                persistence.PayloadStore,
                loader,
                cardAssetExporter,
                nativeSpriteMaterializer,
                publication
            );
            var recovery = new CombatReplayReportStartupRecovery(
                reportDataRoot,
                videoRootDirectoryPath,
                (limit, cursor) => metadataStore.ListCompletedForReportRecovery(limit, cursor),
                publisher,
                () =>
                    CombatReplayReportViewerGate
                        .EnsureInstalledForDataRoot(reportDataRoot)
                        .Artifacts.BundleId
            );
            _pendingReportRecovery = StartCoroutine(
                RecoverIncompleteStaticReportsOverFrames(
                    recovery,
                    L.CurrentLanguageCode,
                    L.CurrentMode == BppChineseLocaleMode.Taiwan
                )
            );
        }
        catch
        {
            // Startup reconciliation is best effort. Recording, replay playback, and later live
            // report publication must remain available if the database or filesystem is damaged.
        }
    }

    private IEnumerator RecoverIncompleteStaticReportsOverFrames(
        CombatReplayReportStartupRecovery recovery,
        string locale,
        bool useTraditionalChinese
    )
    {
        while (!_destroying && !recovery.IsCompleted)
        {
            _ = recovery.RecoverNextBatch(
                StartupReportRecoveryBatchSize,
                locale,
                useTraditionalChinese
            );

            // Candidate discovery and cache inspection are bounded to one database page per
            // frame. Source failures are retried with bounded backoff by the recovery object;
            // startup recovery is cache-only and never admits Unity materialization work.
            yield return null;
        }

        _pendingReportRecovery = null;
    }

    private void Update()
    {
        _persistence?.DrainPendingResults();
        _reportPublication?.DrainTerminals(OnReportPublicationTerminal);

        ObservePendingMenuReturn();

        // The exit-in-progress latch clears itself once ReplayState is actually gone; this is
        // the only reliable signal on the bootstrapped exit path, where the state transition
        // happens via RunManager.ReturnToMainMenu without a normal ReplayState exit event.
        if (_replayExitInProgress && AppState.CurrentState is not ReplayState)
            _replayExitInProgress = false;
    }

    private void OnDestroy()
    {
        ReplayOpeningStateRestorer.Cleanup();
        _destroying = true;
        CancelPendingCurrentReplayStart(
            "native-replay-runtime-destroyed-before-start",
            "Combat replay runtime was destroyed."
        );
        CancelCurrentReplayRecapPostRoll();
        if (_pendingReportRecovery != null)
        {
            StopCoroutine(_pendingReportRecovery);
            _pendingReportRecovery = null;
        }
        CancelReportAssetPreparation();
        _reportCardAssetExporter?.Dispose();
        _reportCardAssetExporter = null;
        _reportNativeSpriteMaterializer?.Dispose();
        _reportNativeSpriteMaterializer = null;
        _reportPublication = null;
        _pendingReportDraftCaptures.Clear();
        _pendingReportAssetManifest = null;
        _pendingReportAssetRecordingId = null;
        _pendingPreparedReportAssets = Array.Empty<PostCombatReportAssetFile>();
        if (_currentRecording.NativeReplayStarted)
        {
            _playbackPublisher?.PublishEnded("runtime-destroyed", failed: true);
            _currentRecording.MarkReplayEnded("Combat replay runtime was destroyed.");
        }
        var operation = _activePlaybackOperation;
        if (operation != null)
        {
            var ended = _playbackPublisher?.PublishEnded("runtime-destroyed", failed: true);
            var failureReason = ended is { Succeeded: false }
                ? ReplayPlaybackReasonCode.EndedPublishFailed
                : ReplayPlaybackReasonCode.StartException;
            CompletePlaybackOperation(
                operation,
                ReplayPlaybackEndReasonCode.RuntimeDestroyed,
                ReplayRollbackStatus.NotRequired,
                failureReason,
                ended?.Exception
            );
        }
        _activePlaybackOperation = null;
        _pendingMenuReturn = null;
        _persistence?.Dispose();
        _recordingStartedSubscription?.Dispose();
        _recordingStartedSubscription = null;
        _recordingCompletedSubscription?.Dispose();
        _recordingCompletedSubscription = null;

        if (Instance == this)
            Instance = null;

        Events.StateChanged.RemoveListener(OnStateChanged);
        Events.ReplayStarted.RemoveListener(OnNativeReplayStarted);
        Events.ReplayEnded.RemoveListener(OnNativeReplayEnded);
    }

    public IReadOnlyList<PvpBattleManifest> ListRecentBattles()
    {
        return _controller?.ListRecentBattles() ?? Array.Empty<PvpBattleManifest>();
    }

    public PvpBattleManifest? GetLatestBattle()
    {
        return _controller?.GetLatestBattle();
    }

    public bool CanReplaySavedCombats(out string reason)
    {
        if (IsReplayStartInProgress)
        {
            reason = "A saved replay is already starting.";
            return false;
        }

        if (_services!.RunContext.IsInGameRun)
        {
            reason =
                "Saved replay playback is only available while you are outside an active gameplay session.";
            return false;
        }

        if (AppState.CurrentState is ReplayState)
        {
            reason = "A replay is already in progress.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool CanReplaySavedBattle(string battleId, out string reason)
    {
        if (string.IsNullOrWhiteSpace(battleId))
        {
            reason = "Select a saved battle to replay.";
            return false;
        }

        if (_controller == null)
        {
            reason = "Combat replay runtime is unavailable.";
            return false;
        }

        if (!CanReplaySavedCombats(out reason))
            return false;

        if (!_controller.HasSavedReplay(battleId))
        {
            reason = "Replay payload for the selected battle is unavailable.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public void ObserveMessage(BazaarGameShared.Infra.Messages.INetMessage message)
    {
        if (_captureService == null || _persistence == null)
            return;

        try
        {
            var artifact = _captureService.Accept(
                message,
                _services!.RunContext.CurrentServerRunId
            );
            if (artifact == null)
                return;

            _currentRecordingManifest = artifact.Manifest;
            _currentRecording.LatchBattle(artifact.Manifest.BattleId);
            CaptureReportDraft(artifact.Manifest, artifact.Payload);
            PrepareCurrentReplayRecordingAvailability();
            _persistence.Enqueue(artifact.Payload, artifact.Manifest);
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                CombatReplayLogEvents.CaptureFailed,
                ex,
                CombatReplayLogEvents.CaptureFailedRunId.Bind(
                    _services?.RunContext.CurrentServerRunId
                ),
                CombatReplayLogEvents.CaptureFailedReasonCode.Bind(
                    ReplayCaptureReasonCode.CaptureOrEnqueueException
                )
            );
        }
    }

    private void CaptureReportDraft(PvpBattleManifest manifest, PvpReplayPayload payload)
    {
        var loader = _loader;
        var reportPublication = _reportPublication;
        if (loader == null || reportPublication == null)
            return;

        // The live capture path does not need the decoded sequence to start playback. Keep both
        // MessagePack decode and report projection away from the Unity network/Update thread.
        TrackReportDraftCapture(
            manifest.BattleId,
            Task.Run(() =>
            {
                try
                {
                    var sequence = loader.Load(payload);
                    return reportPublication.TryCaptureDraft(
                        manifest,
                        sequence.CombatMessage,
                        out _
                    );
                }
                catch
                {
                    // Replay persistence and recording remain usable if report projection degrades.
                    // A missing draft simply prevents a misleading partial HTML from being published.
                    return false;
                }
            })
        );
    }

    private void ScheduleReportDraftCapture(
        PvpBattleManifest manifest,
        BazaarGameShared.Infra.Messages.NetMessageCombatSim combatMessage
    )
    {
        var publication = _reportPublication;
        if (publication == null)
            return;

        TrackReportDraftCapture(
            manifest.BattleId,
            publication.CaptureDraftAsync(manifest, combatMessage)
        );
    }

    private void TrackReportDraftCapture(string battleId, Task<bool> captureTask)
    {
        // Projection and its document-id serialization are pure DTO work. Keep them off Unity's
        // Update thread; only the completed immutable document is retained by the coordinator.
        foreach (
            var completedBattleId in _pendingReportDraftCaptures
                .Where(pair => pair.Value.IsCompleted)
                .Select(pair => pair.Key)
                .ToArray()
        )
        {
            _pendingReportDraftCaptures.Remove(completedBattleId);
        }

        _pendingReportDraftCaptures[battleId] = captureTask;
    }

    internal CurrentReplayRecordingSnapshot GetCurrentReplayRecordingSnapshot()
    {
        RefreshCurrentReplayRecordingAvailability();
        return _currentRecording.Snapshot();
    }

    internal void PrepareCurrentReplayRecordingAvailability()
    {
        var availability = _videoRecorder?.Invoke()?.PrepareCurrentReplayRecordingAvailability();
        if (availability.HasValue)
        {
            _currentRecording.SetAvailability(
                availability.Value.IsReady,
                availability.Value.Reason
            );
        }
    }

    internal bool TryStartCurrentReplayRecording(
        Action invokeNativeReplay,
        Action invokeNativeRecap,
        Action invokeNativeRecapBack,
        out string reason
    )
    {
        if (invokeNativeReplay == null)
            throw new ArgumentNullException(nameof(invokeNativeReplay));
        if (invokeNativeRecap == null)
            throw new ArgumentNullException(nameof(invokeNativeRecap));
        if (invokeNativeRecapBack == null)
            throw new ArgumentNullException(nameof(invokeNativeRecapBack));

        RefreshCurrentReplayRecordingAvailability();
        var snapshot = _currentRecording.Snapshot();
        var manifest = _currentRecordingManifest;
        var recorder = _videoRecorder?.Invoke();
        if (!snapshot.CanStart || manifest == null || recorder == null)
        {
            reason = snapshot.Reason ?? "Video recording is not ready.";
            return false;
        }

        var arm = recorder.TryArmCurrentReplay(manifest.BattleId);
        if (!arm.Succeeded || string.IsNullOrWhiteSpace(arm.RecordingId))
        {
            reason = arm.Reason ?? "Video recording could not be prepared.";
            return false;
        }

        var recordingId = arm.RecordingId;
        if (!_currentRecording.TryArm(recordingId))
        {
            recorder.CancelArmedCurrentReplay(
                recordingId,
                "native-replay-state-changed-before-arm"
            );
            reason = "The replay recording state changed before it could start.";
            return false;
        }

        _pendingReportAssetManifest = manifest;
        _pendingReportAssetRecordingId = recordingId;
        _pendingPreparedReportAssets = Array.Empty<PostCombatReportAssetFile>();
        _playbackPublisher!.BeginSession(
            manifest.BattleId,
            manifest,
            CombatReplayPlaybackSource.CurrentNative,
            recordVideo: true
        );
        _invokeCurrentRecordingRecap = invokeNativeRecap;

        var boardManager = Singleton<BoardManager>.Instance;
        var closeRecapFirst =
            boardManager is { } && (boardManager.IsRecapViewOpen || boardManager.StorageMoving);
        try
        {
            _pendingCurrentReplayStart = StartCoroutine(
                StartCurrentReplayWhenReady(
                    recordingId,
                    manifest,
                    invokeNativeReplay,
                    invokeNativeRecapBack,
                    closeRecapFirst
                )
            );
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            FailCurrentReplayStart(
                recordingId,
                "native-replay-preparation-start-failed",
                ex.Message
            );
            reason = ex.Message;
            return false;
        }
    }

    private IEnumerator StartCurrentReplayWhenReady(
        string recordingId,
        PvpBattleManifest manifest,
        Action invokeNativeReplay,
        Action invokeNativeRecapBack,
        bool closeRecapFirst
    )
    {
        if (closeRecapFirst)
        {
            const float recapCloseTimeoutSeconds = 10f;
            var timeoutAt = Time.realtimeSinceStartup + recapCloseTimeoutSeconds;
            while (true)
            {
                if (AppState.CurrentState is not ReplayState)
                {
                    _pendingCurrentReplayStart = null;
                    FailCurrentReplayStart(
                        recordingId,
                        "native-replay-state-exited-before-start",
                        "Replay state exited while the recap view was closing."
                    );
                    yield break;
                }

                var boardManager = Singleton<BoardManager>.Instance;
                if (boardManager == null)
                {
                    _pendingCurrentReplayStart = null;
                    FailCurrentReplayStart(
                        recordingId,
                        "native-replay-board-unavailable",
                        "The combat board is unavailable."
                    );
                    yield break;
                }

                if (
                    boardManager.IsRecapViewOpen
                    && !boardManager.StorageMoving
                    && !AppState.BlockInput
                )
                {
                    try
                    {
                        invokeNativeRecapBack();
                    }
                    catch (Exception ex)
                    {
                        _pendingCurrentReplayStart = null;
                        FailCurrentReplayStart(
                            recordingId,
                            "native-recap-back-invoke-failed",
                            ex.Message
                        );
                        yield break;
                    }
                }

                if (!boardManager.IsRecapViewOpen && !boardManager.StorageMoving)
                    break;

                if (Time.realtimeSinceStartup >= timeoutAt)
                {
                    _pendingCurrentReplayStart = null;
                    FailCurrentReplayStart(
                        recordingId,
                        "native-recap-close-timeout",
                        "The recap view did not finish closing."
                    );
                    yield break;
                }

                yield return null;
            }
        }

        IReadOnlyList<PostCombatReportAssetFile> preparedAssets =
            Array.Empty<PostCombatReportAssetFile>();
        var preparation = PrepareReportAssets(manifest, assets => preparedAssets = assets);
        while (preparation.MoveNext())
            yield return preparation.Current;
        (preparation as IDisposable)?.Dispose();
        _pendingPreparedReportAssets = preparedAssets;

        _pendingCurrentReplayStart = null;
        TryInvokeCurrentReplay(recordingId, invokeNativeReplay, out _);
    }

    private bool TryInvokeCurrentReplay(
        string recordingId,
        Action invokeNativeReplay,
        out string reason
    )
    {
        try
        {
            invokeNativeReplay();
        }
        catch (Exception ex)
        {
            FailCurrentReplayStart(recordingId, "native-replay-invoke-failed", ex.Message);
            reason = ex.Message;
            return false;
        }

        if (!_currentRecording.NativeReplayStarted)
        {
            FailCurrentReplayStart(
                recordingId,
                "native-replay-not-started",
                "The native replay did not start."
            );
            reason = "The native replay did not start.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void FailCurrentReplayStart(string recordingId, string endReason, string reason)
    {
        _videoRecorder?.Invoke()?.CancelArmedCurrentReplay(recordingId, endReason);
        _playbackPublisher?.PublishEnded(endReason, failed: true);
        _currentRecording.RollbackArm(recordingId, reason);
        _reportCardAssetExporter?.Cancel();
        _reportNativeSpriteMaterializer?.Cancel();
        _pendingReportAssetManifest = null;
        _pendingReportAssetRecordingId = null;
        _pendingPreparedReportAssets = Array.Empty<PostCombatReportAssetFile>();
        _invokeCurrentRecordingRecap = null;
    }

    private void CancelPendingCurrentReplayStart(string endReason, string reason)
    {
        var pending = _pendingCurrentReplayStart;
        if (pending == null)
            return;

        _pendingCurrentReplayStart = null;
        StopCoroutine(pending);
        var recordingId = _currentRecording.RecordingId;
        if (!string.IsNullOrWhiteSpace(recordingId))
            FailCurrentReplayStart(recordingId, endReason, reason);
    }

    internal bool TryRevealCurrentReplayVideo(out string reason)
    {
        var snapshot = _currentRecording.Snapshot();
        if (!snapshot.CanReveal || string.IsNullOrWhiteSpace(snapshot.FinalFilePath))
        {
            reason = snapshot.Reason ?? "Recorded video is unavailable.";
            return false;
        }

        return SystemFileRevealer.TryReveal(snapshot.FinalFilePath, out reason);
    }

    internal bool TryOpenCurrentReplayReport(out string reason)
    {
        var snapshot = _currentRecording.Snapshot();
        var reportRootDirectoryPath = GetReportRootDirectoryPath();
        reason = snapshot.Reason ?? "Battle report is unavailable.";
        if (
            !snapshot.CanOpenReport
            || !CombatReportRecordingId.TryParse(snapshot.RecordingId, out var recordingId)
            || string.IsNullOrWhiteSpace(reportRootDirectoryPath)
        )
        {
            return false;
        }

        if (
            !SystemReportOpener.TryResolve(
                reportRootDirectoryPath,
                recordingId,
                out var resolvedReport,
                out reason
            )
            || !CombatReplayReportViewerGate.TryEnsureInstalledForReport(
                resolvedReport!.ReportRootDirectoryPath,
                resolvedReport.FullPath,
                out reason
            )
        )
        {
            return false;
        }

        return SystemReportOpener.TryOpen(resolvedReport!, out reason);
    }

    private void RefreshCurrentReplayRecordingAvailability()
    {
        var availability = _videoRecorder?.Invoke()?.GetCurrentReplayRecordingAvailability();
        if (!availability.HasValue)
            return;

        _currentRecording.SetAvailability(availability.Value.IsReady, availability.Value.Reason);
    }

    private void OnReplayPersistenceCompleted(
        PvpBattleManifest manifest,
        bool succeeded,
        Exception? error
    )
    {
        if (_destroying)
            return;
        _currentRecording.MarkBattlePersistence(manifest.BattleId, succeeded, error?.Message);
        if (succeeded)
            PrepareCurrentReplayRecordingAvailability();
    }

    private void OnNativeReplayStarted()
    {
        if (!_currentRecording.MarkNativeReplayStarted())
            return;

        var outcome = _playbackPublisher?.PublishStarting();
        if (outcome is { Succeeded: false })
        {
            _playbackPublisher?.PublishEnded("starting-publish-failed", failed: true);
            _currentRecording.MarkReplayEnded(outcome.Value.Exception?.Message);
        }
    }

    private void OnNativeReplayEnded()
    {
        if (!_currentRecording.NativeReplayStarted)
        {
            ReplayPlaybackNativeEndCoordinator.TryFinalizeRecording(
                _activePlaybackOperation,
                () =>
                    _playbackPublisher?.PublishEnded("recorded-saved-replay-ended", failed: false)
                    ?? ReplayPlaybackPublishOutcome.Success()
            );
            return;
        }

        var invokeNativeRecap = _invokeCurrentRecordingRecap;
        _invokeCurrentRecordingRecap = null;
        if (invokeNativeRecap == null)
        {
            CompleteCurrentReplayRecording(
                "native-recap-action-unavailable",
                failed: true,
                "The native recap action is unavailable."
            );
            return;
        }

        try
        {
            invokeNativeRecap();
            if (Singleton<BoardManager>.Instance?.IsRecapViewOpen != true)
            {
                CompleteCurrentReplayRecording(
                    "native-recap-not-started",
                    failed: true,
                    "The native recap did not start."
                );
                return;
            }
            _pendingCurrentReplayRecapPostRoll = StartCoroutine(
                CompleteCurrentReplayRecordingAfterRecapPostRoll()
            );
        }
        catch (Exception ex)
        {
            CompleteCurrentReplayRecording("native-recap-invoke-failed", failed: true, ex.Message);
        }
    }

    private IEnumerator CompleteCurrentReplayRecordingAfterRecapPostRoll()
    {
        yield return new WaitForSecondsRealtime(CurrentReplayRecapPostRollSeconds);
        _pendingCurrentReplayRecapPostRoll = null;
        CompleteCurrentReplayRecording(
            "native-replay-recap-post-roll-ended",
            failed: false,
            reason: null
        );
    }

    private void CompleteCurrentReplayRecording(string endReason, bool failed, string? reason)
    {
        var outcome = _playbackPublisher?.PublishEnded(endReason, failed);
        _currentRecording.MarkReplayEnded(
            outcome is { Succeeded: false } ? outcome.Value.Exception?.Message : reason
        );
    }

    private void CancelCurrentReplayRecapPostRoll()
    {
        var pending = _pendingCurrentReplayRecapPostRoll;
        if (pending == null)
            return;

        _pendingCurrentReplayRecapPostRoll = null;
        StopCoroutine(pending);
    }

    private void OnVideoRecordingStarted(CombatReplayVideoRecordingStarted started)
    {
        _currentRecording.MarkRecordingStarted(started.RecordingId, started.BattleId);
        _reportPublication?.ObserveVideoStarted(started);

        var pendingManifest = _pendingReportAssetManifest;
        if (
            pendingManifest != null
            && string.Equals(pendingManifest.BattleId, started.BattleId, StringComparison.Ordinal)
            && (
                string.IsNullOrWhiteSpace(_pendingReportAssetRecordingId)
                || string.Equals(
                    _pendingReportAssetRecordingId,
                    started.RecordingId,
                    StringComparison.Ordinal
                )
            )
        )
        {
            _pendingReportAssetRecordingId = started.RecordingId;
            _reportPublication?.MarkAssetsReady(
                started.RecordingId,
                started.BattleId,
                _pendingPreparedReportAssets
            );
            _pendingReportAssetManifest = null;
            _pendingReportAssetRecordingId = null;
            _pendingPreparedReportAssets = Array.Empty<PostCombatReportAssetFile>();
        }
    }

    private void OnVideoRecordingCompleted(CombatReplayVideoRecordingCompleted completed)
    {
        _currentRecording.ApplyCompletion(completed);
        _reportPublication?.ObserveVideoTerminal(
            completed,
            L.CurrentLanguageCode,
            L.CurrentMode == BppChineseLocaleMode.Taiwan
        );
    }

    private void OnReportPublicationTerminal(CombatReplayReportPublicationTerminal terminal)
    {
        var reportRootDirectoryPath = GetReportRootDirectoryPath();
        if (
            !terminal.Succeeded
            || string.IsNullOrWhiteSpace(reportRootDirectoryPath)
            || !CombatReportRecordingId.TryParse(terminal.RecordingId, out var recordingId)
            || !SystemReportOpener.TryResolve(
                reportRootDirectoryPath,
                recordingId,
                out var resolvedReport,
                out _
            )
        )
        {
            // The persisted video remains the current-completion fallback when report
            // publication fails or its committed HTML is no longer physical.
            return;
        }

        _currentRecording.ApplyReportCompletion(
            terminal.RecordingId,
            terminal.BattleId,
            resolvedReport!.FullPath
        );
    }

    private static string GetReportRootDirectoryPath() =>
        Path.Combine(BepInEx.Paths.GameRootPath, "BazaarPlusPlusV4", "reports");

    private IEnumerator PrepareReportAssets(
        PvpBattleManifest manifest,
        Action<IReadOnlyList<PostCombatReportAssetFile>> onCompleted
    )
    {
        var cardAssetExporter = _reportCardAssetExporter;
        var nativeSpriteMaterializer = _reportNativeSpriteMaterializer;
        CombatReportDocumentV1? reportDocument = null;
        if (_pendingReportDraftCaptures.TryGetValue(manifest.BattleId, out var draftCaptureTask))
        {
            while (!draftCaptureTask.IsCompleted)
                yield return null;
            if (draftCaptureTask.IsFaulted)
                _ = draftCaptureTask.Exception;
            _pendingReportDraftCaptures.Remove(manifest.BattleId);
        }

        Task<CombatReportDocumentV1?>? draftSnapshotTask = null;
        try
        {
            draftSnapshotTask = _reportPublication?.GetDraftSnapshotAsync(manifest.BattleId);
        }
        catch
        {
            // Missing report projection degrades only native report decoration.
        }
        if (draftSnapshotTask != null)
        {
            while (!draftSnapshotTask.IsCompleted)
                yield return null;
            if (draftSnapshotTask.Status == TaskStatus.RanToCompletion)
                reportDocument = draftSnapshotTask.Result;
            else if (draftSnapshotTask.IsFaulted)
                _ = draftSnapshotTask.Exception;
        }

        if (cardAssetExporter != null)
        {
            var operation = DrainReportAssetOperation(
                manifest.BattleId,
                cardAssetExporter.MaterializeBattle(manifest, () => { })
            );
            using (operation as IDisposable)
            {
                while (operation.MoveNext())
                    yield return operation.Current;
            }
        }

        if (nativeSpriteMaterializer != null && reportDocument != null)
        {
            var operation = DrainReportAssetOperation(
                manifest.BattleId,
                nativeSpriteMaterializer.MaterializeBattle(manifest, reportDocument, () => { })
            );
            using (operation as IDisposable)
            {
                while (operation.MoveNext())
                    yield return operation.Current;
            }
        }

        var assets = new List<PostCombatReportAssetFile>();
        Task<IReadOnlyList<PostCombatReportAssetFile>>? cardAssetsTask = null;
        try
        {
            if (cardAssetExporter != null)
                cardAssetsTask = cardAssetExporter.ListAvailableCardAssetsAsync(manifest);
        }
        catch
        {
            // A corrupt optional cache mapping degrades only that entity to a placeholder.
        }
        Task<IReadOnlyList<PostCombatReportAssetFile>>? nativeAssetsTask = null;
        try
        {
            if (nativeSpriteMaterializer != null && reportDocument != null)
            {
                nativeAssetsTask = nativeSpriteMaterializer.ListAvailableAssetsAsync(
                    manifest,
                    reportDocument
                );
            }
        }
        catch
        {
            // Native hero/status cache bindings are optional report decoration.
        }

        while (
            cardAssetsTask is { IsCompleted: false } || nativeAssetsTask is { IsCompleted: false }
        )
        {
            yield return null;
        }
        if (cardAssetsTask is { Status: TaskStatus.RanToCompletion })
            assets.AddRange(cardAssetsTask.Result);
        else if (cardAssetsTask is { IsFaulted: true })
            _ = cardAssetsTask.Exception;
        if (nativeAssetsTask is { Status: TaskStatus.RanToCompletion })
            assets.AddRange(nativeAssetsTask.Result);
        else if (nativeAssetsTask is { IsFaulted: true })
            _ = nativeAssetsTask.Exception;

        onCompleted(assets);
    }

    private static IEnumerator DrainReportAssetOperation(string? battleId, IEnumerator operation)
    {
        using (operation as IDisposable)
        {
            while (true)
            {
                bool hasCurrent;
                object? current = null;
                Exception? failure = null;
                try
                {
                    hasCurrent = operation.MoveNext();
                    if (hasCurrent)
                        current = operation.Current;
                }
                catch (Exception ex)
                {
                    hasCurrent = false;
                    failure = ex;
                }

                if (failure != null)
                {
                    PostCombatReportAssetDiagnostics.ReportFailure(
                        battleId,
                        null,
                        PostCombatReportCardPreviewReasonCode.Exception,
                        null,
                        exception: failure
                    );
                    yield break;
                }

                if (!hasCurrent)
                    yield break;
                yield return current;
            }
        }
    }

    private Task<IReadOnlyList<PostCombatReportAssetFile>> PrepareReportAssetsAsync(
        PvpBattleManifest manifest
    )
    {
        if (_pendingReportAssetPreparation != null)
            throw new InvalidOperationException("Report asset preparation is already running.");

        var completion = new TaskCompletionSource<IReadOnlyList<PostCombatReportAssetFile>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _reportAssetPreparationCompletion = completion;
        _pendingReportAssetPreparation = StartCoroutine(
            PrepareReportAssetsForSavedReplay(manifest, completion)
        );
        return completion.Task;
    }

    private IEnumerator PrepareReportAssetsForSavedReplay(
        PvpBattleManifest manifest,
        TaskCompletionSource<IReadOnlyList<PostCombatReportAssetFile>> completion
    )
    {
        // Ensure StartCoroutine has assigned its handle before a cache-only pass can finish.
        yield return null;
        IReadOnlyList<PostCombatReportAssetFile> assets = Array.Empty<PostCombatReportAssetFile>();
        var operation = PrepareReportAssets(manifest, prepared => assets = prepared);
        while (operation.MoveNext())
            yield return operation.Current;
        (operation as IDisposable)?.Dispose();

        _pendingReportAssetPreparation = null;
        _reportAssetPreparationCompletion = null;
        completion.TrySetResult(assets);
    }

    private void CancelReportAssetPreparation()
    {
        var pending = _pendingReportAssetPreparation;
        if (pending != null)
            StopCoroutine(pending);
        _pendingReportAssetPreparation = null;
        _reportCardAssetExporter?.Cancel();
        _reportNativeSpriteMaterializer?.Cancel();
        _reportAssetPreparationCompletion?.TrySetResult(Array.Empty<PostCombatReportAssetFile>());
        _reportAssetPreparationCompletion = null;
    }

    public bool ReplayLatest()
    {
        var latest = _controller?.GetLatestBattle();
        if (latest == null)
            return false;

        return ReplaySaved(latest.BattleId, recordVideo: false);
    }

    public bool ReplaySaved(string battleId, bool recordVideo)
    {
        if (!CanReplaySavedBattle(battleId, out _))
        {
            LogRequestRejected(
                CombatReplayPlaybackSource.LocalSaved,
                ResolveSavedReplayRejectionReason(battleId),
                battleId
            );
            return false;
        }

        var controller = _controller;
        if (controller == null)
            return false;

        var manifest = controller.LoadBattle(battleId);
        if (manifest == null)
        {
            LogRequestRejected(
                CombatReplayPlaybackSource.LocalSaved,
                ReplayRequestRejectionReasonCode.ManifestUnavailable,
                battleId
            );
            return false;
        }

        var payload = controller.LoadPayload(manifest);
        if (payload == null)
        {
            LogRequestRejected(
                CombatReplayPlaybackSource.LocalSaved,
                ReplayRequestRejectionReasonCode.PayloadUnavailable,
                battleId
            );
            return false;
        }

        var operation = new ReplayPlaybackLogOperation(
            battleId,
            CombatReplayPlaybackSource.LocalSaved,
            recordVideo
        );
        _activePlaybackOperation = operation;
        CombatSequenceMessages sequence;
        try
        {
            sequence = controller.LoadReplay(payload);
            ScheduleReportDraftCapture(manifest, sequence.CombatMessage);
        }
        catch (Exception ex)
        {
            CompletePlaybackOperation(
                operation,
                ReplayPlaybackEndReasonCode.StartFailed,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.StartException,
                ex
            );
            return false;
        }

        PlaybackUiState.InitializedBoardUiControllers.Clear();
        _savedReplayProgress = SavedReplayProgress.SavedPlaybackActive;
        _startupInterruptionReason = ReplayPlaybackReasonCode.None;
        _startupInterruptionException = null;
        _ = StartReplayAsync(
            manifest,
            sequence,
            battleId,
            CombatReplayPlaybackSource.LocalSaved,
            recordVideo,
            operation
        );
        return true;
    }

    public bool ReplayImportedBattle(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        bool recordVideo
    )
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        if (!CanReplaySavedCombats(out _))
        {
            LogRequestRejected(
                CombatReplayPlaybackSource.ImportedGhost,
                ResolveGeneralReplayRejectionReason(),
                manifest.BattleId
            );
            return false;
        }

        var loader = _loader;
        if (loader == null)
        {
            LogRequestRejected(
                CombatReplayPlaybackSource.ImportedGhost,
                ReplayRequestRejectionReasonCode.LoaderUnavailable,
                manifest.BattleId
            );
            return false;
        }

        var operation = new ReplayPlaybackLogOperation(
            manifest.BattleId,
            CombatReplayPlaybackSource.ImportedGhost,
            recordVideo
        );
        _activePlaybackOperation = operation;
        CombatSequenceMessages sequence;
        try
        {
            sequence = loader.Load(payload);
            ScheduleReportDraftCapture(manifest, sequence.CombatMessage);
        }
        catch (Exception ex)
        {
            CompletePlaybackOperation(
                operation,
                ReplayPlaybackEndReasonCode.StartFailed,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.StartException,
                ex
            );
            return false;
        }

        PlaybackUiState.InitializedBoardUiControllers.Clear();
        _savedReplayProgress = SavedReplayProgress.SavedPlaybackActive;
        _startupInterruptionReason = ReplayPlaybackReasonCode.None;
        _startupInterruptionException = null;
        _ = StartReplayAsync(
            manifest,
            sequence,
            manifest.BattleId,
            CombatReplayPlaybackSource.ImportedGhost,
            recordVideo,
            operation
        );
        return true;
    }

    /// <summary>
    /// Drives the replay "continue" button programmatically: validates that playback has finished
    /// and is waiting on the button, then runs the same chain a real click does
    /// (BoardManager.OnBoardRecapReplayButtonsContinueClicked: LevelUp recap cleanup, then
    /// <c>ReplayState.Exit()</c>). This is the only programmatic path allowed to exit ReplayState;
    /// recorded saved replays normally finalize at native ReplayEnded, while state exit remains the
    /// idempotent lifecycle fallback.
    /// </summary>
    public bool TryContinueReplay(out string reason)
    {
        if (AppState.CurrentState is not ReplayState replay)
        {
            reason = "No replay is active.";
            return false;
        }

        if (IsReplayStartInProgress)
        {
            reason = "Replay playback is still starting.";
            return false;
        }

        if (_pendingCurrentReplayRecapPostRoll != null)
        {
            reason = "Replay recording is still capturing the recap.";
            return false;
        }

        if (replay.IsReplaying)
        {
            reason = "Replay playback has not finished yet.";
            return false;
        }

        if (IsReplayExitSuppressionActive)
        {
            reason = "Replay exit is already in progress.";
            return false;
        }

        // Mirror the native continue click: clear the LevelUp recap overlay first
        // (BoardManager.OnBoardRecapReplayButtonsContinueClicked guards on ERunState.LevelUp),
        // then Exit(). For bootstrapped saved replays the Exit() prefix patch reroutes into
        // TryExitBootstrappedSavedReplayToMenu, which closes any still-active playback session.
        if (Data.CurrentState?.StateName == BazaarGameShared.Domain.Runs.ERunState.LevelUp)
            Singleton<BoardManager>.Instance?.ExitRecapReplayState();

        replay.Exit();
        LatchReplayExitInProgress();
        reason = string.Empty;
        return true;
    }

    private async Task StartReplayAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        string battleId,
        CombatReplayPlaybackSource source,
        bool recordVideo,
        ReplayPlaybackLogOperation operation
    )
    {
        var attemptedBootstrapFromLobby = false;
        _savedReplayProgress = SavedReplayProgress.StartInProgress;
        _pendingReportAssetManifest = recordVideo ? manifest : null;
        _pendingReportAssetRecordingId = null;
        _pendingPreparedReportAssets = Array.Empty<PostCombatReportAssetFile>();
        _playbackPublisher!.BeginSession(battleId, manifest, source, recordVideo);
        try
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            ReplayOpeningStateRestorer.Cleanup();
            _portraitController!.Cleanup(battleId);
            _portraitController.ApplySelectedHeroOverride(manifest);
            Data.ResetRunData();
            _runLifecycle!.RefreshRunStateFromCurrentState();
            attemptedBootstrapFromLobby = !ReplayBootstrap.IsBootstrapReady();
            var bootstrappedFromLobby = await ReplayBootstrap.EnsureBootstrapReadyAsync();
            _returnToMenuAfterReplay = bootstrappedFromLobby;
            var bootstrapContext = ReplayBootstrap.ResolveDependencies(operation);
            try
            {
                OpponentPortraitController.EnsureOpponentIdentity(
                    manifest,
                    sequence.SpawnMessage,
                    operation
                );
            }
            catch (Exception ex)
            {
                operation.ReportDegradation(
                    ReplayPlaybackReasonCode.OpponentIdentityUnavailable,
                    ex
                );
            }
            ReplayRunEconomyFallback.ApplyMissingRunEconomy(
                manifest,
                _services?.Paths.RunLogDatabasePath,
                operation
            );
            if (recordVideo)
                _pendingPreparedReportAssets = await PrepareReportAssetsAsync(manifest);
            await ReplayBootstrap.InjectSavedReplayAsync(
                bootstrapContext,
                manifest,
                sequence,
                operation,
                () => PrepareSavedReplayNativePresentationAsync(manifest, operation),
                _playbackPublisher.PublishStarting
            );
            if (_startupInterruptionReason != ReplayPlaybackReasonCode.None)
            {
                throw new ReplayPlaybackStartInterruptedException(
                    _startupInterruptionReason,
                    _startupInterruptionException
                );
            }
            _bootstrappedReplayActive = bootstrappedFromLobby;
            if (operation.TryMarkStarted(out var started))
                ReplayPlaybackLogWriter.EmitStarted(started);
        }
        catch (Exception ex)
        {
            CancelReportAssetPreparation();
            _pendingReportAssetManifest = null;
            _pendingReportAssetRecordingId = null;
            _pendingPreparedReportAssets = Array.Empty<PostCombatReportAssetFile>();
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            _savedReplayProgress = SavedReplayProgress.StartFailureCleanup;
            // Unconditional: PublishEnded only publishes the event when "starting" was
            // published, but it must always clear the session (battle id) for a failed start.
            var ended = ReplayPlaybackStateExitCoordinator.Handle(
                startCoordinatorOwnsTerminal: false,
                () => _playbackPublisher!.PublishEnded("start-failed", failed: true),
                latchStartupInterruption: null,
                (stage, cleanupException) =>
                    LogCleanupFailure(stage, operation.BattleId, cleanupException),
                new ReplayPlaybackCleanupStep(
                    "opponent_portrait",
                    () => _portraitController!.Cleanup(battleId)
                ),
                new ReplayPlaybackCleanupStep(
                    "hero_restore",
                    () => _portraitController!.RestoreSelectedHeroOverride()
                ),
                new ReplayPlaybackCleanupStep("opening_state", ReplayOpeningStateRestorer.Cleanup)
            );
            var failureReason =
                ex is ReplayPlaybackPublishException publishException ? publishException.ReasonCode
                : ex is ReplayPlaybackStartInterruptedException interrupted ? interrupted.ReasonCode
                : ReplayPlaybackReasonCode.StartException;
            var failureException =
                ex is ReplayPlaybackStartInterruptedException
                    ? ex.InnerException
                    : ex.InnerException ?? ex;
            if (!ended.Succeeded)
            {
                failureReason = ReplayPlaybackReasonCode.EndedPublishFailed;
                failureException = ended.Exception;
            }
            var rollbackStatus = ReplayRollbackStatus.NotRequired;
            if (attemptedBootstrapFromLobby)
            {
                var rollback = await ReplayBootstrap.RollbackBootstrapAsync(operation);
                rollbackStatus = rollback.Succeeded
                    ? ReplayRollbackStatus.Succeeded
                    : ReplayRollbackStatus.Failed;
                if (!rollback.Succeeded)
                {
                    failureReason = ReplayPlaybackReasonCode.BootstrapRollbackFailed;
                    failureException = rollback.Exception;
                }
            }
            CompletePlaybackOperation(
                operation,
                ReplayPlaybackEndReasonCode.StartFailed,
                rollbackStatus,
                failureReason,
                failureException
            );
        }
        finally
        {
            _startupInterruptionReason = ReplayPlaybackReasonCode.None;
            _startupInterruptionException = null;
            _savedReplayProgress = _savedReplayProgress switch
            {
                SavedReplayProgress.StartInProgress => SavedReplayProgress.SavedPlaybackActive,
                SavedReplayProgress.StartFailureCleanup => SavedReplayProgress.Idle,
                _ => _savedReplayProgress,
            };
        }
    }

    private async Task PrepareSavedReplayNativePresentationAsync(
        PvpBattleManifest manifest,
        ReplayPlaybackLogOperation operation
    )
    {
        try
        {
            await ReplayNativeBoardPresentation.RebuildOpponentCollectiblesAsync();
        }
        catch (Exception ex)
        {
            operation.ReportDegradation(
                ReplayPlaybackReasonCode.OpponentCollectiblesUnavailable,
                ex
            );
        }

        try
        {
            await _portraitController!.EnsureTemporaryOpponentPortraitAsync(manifest, operation);
        }
        catch (Exception ex)
        {
            operation.ReportDegradation(ReplayPlaybackReasonCode.OpponentPortraitUnavailable, ex);
        }

        try
        {
            // The plugin's replay controls remain visible even though the native replay/recap
            // buttons are suppressed. Treat that occupied layer as authoritative so native bank
            // and related opponent chrome cannot cover the controls.
            ReplayNativeBoardPresentation.Normalize(replayControlsVisible: true);
            ReplayNativeBoardPresentation.Observe(operation.BattleId);
        }
        catch (Exception ex)
        {
            operation.ReportDegradation(ReplayPlaybackReasonCode.PresentationWarmupFailed, ex);
        }
    }

    private void OnStateChanged(StateChangedEvent data)
    {
        if (data == null)
            return;

        if (data.PreviousState is not ReplayState && data.CurrentState is ReplayState)
        {
            _currentRecording.EnterReplayState();
            PrepareCurrentReplayRecordingAvailability();
            return;
        }

        if (data.PreviousState is not ReplayState || data.CurrentState is ReplayState)
            return;

        CancelPendingCurrentReplayStart(
            "native-replay-state-exited-before-start",
            "Replay state exited before the native replay could start."
        );
        CancelCurrentReplayRecapPostRoll();
        if (_currentRecording.NativeReplayStarted)
        {
            var currentEnded = _playbackPublisher?.PublishEnded("replay-state-exit", failed: true);
            _currentRecording.MarkReplayEnded(
                currentEnded is { Succeeded: false }
                    ? currentEnded.Value.Exception?.Message
                    : "Replay state exited before the native replay ended."
            );
        }
        _currentRecording.LeaveReplayState();
        _currentRecordingManifest = null;
        _invokeCurrentRecordingRecap = null;

        var startCoordinatorOwnsTerminal =
            _savedReplayProgress
            is SavedReplayProgress.StartInProgress
                or SavedReplayProgress.StartFailureCleanup;
        _savedReplayProgress = _savedReplayProgress switch
        {
            SavedReplayProgress.StartInProgress => SavedReplayProgress.StartFailureCleanup,
            SavedReplayProgress.SavedPlaybackActive => SavedReplayProgress.Idle,
            _ => _savedReplayProgress,
        };
        var operation = _activePlaybackOperation;
        var ended = ReplayPlaybackStateExitCoordinator.Handle(
            startCoordinatorOwnsTerminal,
            () =>
                _playbackPublisher?.PublishEnded("state-exit", failed: startCoordinatorOwnsTerminal)
                ?? ReplayPlaybackPublishOutcome.Success(),
            (reason, exception) =>
            {
                _startupInterruptionReason = reason;
                _startupInterruptionException = exception;
            },
            (stage, exception) => LogCleanupFailure(stage, operation?.BattleId, exception),
            new ReplayPlaybackCleanupStep(
                "hero_restore",
                () => _portraitController?.RestoreSelectedHeroOverride()
            ),
            new ReplayPlaybackCleanupStep(
                "opponent_portrait",
                () => _portraitController?.Cleanup(operation?.BattleId)
            ),
            new ReplayPlaybackCleanupStep(
                "playback_ui",
                PlaybackUiState.InitializedBoardUiControllers.Clear
            ),
            new ReplayPlaybackCleanupStep("opening_state", ReplayOpeningStateRestorer.Cleanup)
        );

        if (startCoordinatorOwnsTerminal)
            return;

        if (_pendingMenuReturn != null)
            return;

        if (!_returnToMenuAfterReplay || !_bootstrappedReplayActive)
        {
            if (operation != null)
            {
                CompletePlaybackOperation(
                    operation,
                    ReplayPlaybackEndReasonCode.StateExit,
                    ReplayRollbackStatus.NotRequired,
                    !ended.Succeeded
                        ? ReplayPlaybackReasonCode.EndedPublishFailed
                        : ReplayPlaybackReasonCode.None,
                    ended.Exception
                );
            }
            return;
        }

        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;

        if (operation != null)
        {
            BeginPendingMenuReturn(
                operation,
                ReplayPlaybackEndReasonCode.StateExit,
                !ended.Succeeded
                    ? ReplayPlaybackReasonCode.EndedPublishFailed
                    : ReplayPlaybackReasonCode.None,
                !ended.Succeeded ? ended.Exception : null
            );
        }
    }

    private static void LogCleanupFailure(string stage, string? battleId, Exception exception)
    {
        BppLog.DebugEvent(
            CombatReplayLogEvents.PlaybackCleanupObserved,
            exception,
            () =>
                [
                    CombatReplayLogEvents.CleanupObservedStage.Bind(stage),
                    CombatReplayLogEvents.CleanupObservedRemovedCount.Bind(0),
                    CombatReplayLogEvents.CleanupObservedBattleId.Bind(battleId),
                ]
        );
    }

    internal static bool TryHandleReplayStateExit()
    {
        return TryExitBootstrappedSavedReplayToMenu();
    }

    internal static bool TryExitBootstrappedSavedReplayToMenu()
    {
        var instance = Instance;
        if (instance == null)
            return false;

        // A replay exit is already in flight (the bootstrapped flags were cleared, but the
        // async menu-return has not left ReplayState yet). Report "handled" so the Exit()
        // prefix patch suppresses the original body — running it now would dispatch the dead
        // replay's despawn GameSim into the live state machine mid transition. Time-bounded:
        // see ReplayExitSuppressionWindowSeconds.
        if (instance.IsReplayExitSuppressionActive && AppState.CurrentState is ReplayState)
            return true;

        if (!instance.IsSavedReplayPlaybackActive || !instance._bootstrappedReplayActive)
            return false;

        instance.ExitBootstrappedSavedReplayToMenu();
        return true;
    }

    private void ExitBootstrappedSavedReplayToMenu()
    {
        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;
        _savedReplayProgress = SavedReplayProgress.Idle;
        // Covers the native continue-click path too (it never goes through TryContinueReplay);
        // Update() clears the latch once ReplayState is actually gone.
        LatchReplayExitInProgress();
        // Bootstrapped saved replays exit through this manual path (the state-exit patch
        // intercepts the normal transition), so OnStateChanged's PublishEnded never fires for
        // them. Recorded sessions normally publish at native ReplayEnded; keep this idempotent
        // publish as the exit fallback and to clear non-recorded playback sessions.
        var operation = _activePlaybackOperation;
        var ended = ReplayPlaybackStateExitCoordinator.Handle(
            startCoordinatorOwnsTerminal: false,
            () =>
                _playbackPublisher?.PublishEnded("saved-replay-exit", failed: false)
                ?? ReplayPlaybackPublishOutcome.Success(),
            latchStartupInterruption: null,
            (stage, exception) => LogCleanupFailure(stage, operation?.BattleId, exception),
            new ReplayPlaybackCleanupStep(
                "hero_restore",
                () => _portraitController?.RestoreSelectedHeroOverride()
            ),
            new ReplayPlaybackCleanupStep(
                "opponent_portrait",
                () => _portraitController?.Cleanup(operation?.BattleId)
            ),
            new ReplayPlaybackCleanupStep(
                "playback_ui",
                PlaybackUiState.InitializedBoardUiControllers.Clear
            ),
            new ReplayPlaybackCleanupStep("opening_state", ReplayOpeningStateRestorer.Cleanup)
        );

        if (operation != null)
        {
            BeginPendingMenuReturn(
                operation,
                ReplayPlaybackEndReasonCode.SavedReplayExit,
                !ended.Succeeded
                    ? ReplayPlaybackReasonCode.EndedPublishFailed
                    : ReplayPlaybackReasonCode.None,
                !ended.Succeeded ? ended.Exception : null
            );
        }
    }

    private void BeginPendingMenuReturn(
        ReplayPlaybackLogOperation operation,
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayPlaybackReasonCode priorFailureReason,
        Exception? priorException
    )
    {
        var dispatch = TryBeginReturnToMainMenu();
        if (!dispatch.Succeeded)
        {
            CompletePlaybackOperation(
                operation,
                endReasonCode,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.MenuReturnFailed,
                dispatch.Exception
            );
            return;
        }

        _pendingMenuReturn = new PendingReplayMenuReturn(
            operation,
            endReasonCode,
            priorFailureReason,
            priorException,
            Time.realtimeSinceStartup + ReplayExitSuppressionWindowSeconds
        );
    }

    private void ObservePendingMenuReturn()
    {
        var pending = _pendingMenuReturn;
        if (pending == null)
            return;

        if (SceneLoader.IsSceneLoaded(SceneID.HeroSelectScene))
        {
            _pendingMenuReturn = null;
            CompletePlaybackOperation(
                pending.Operation,
                pending.EndReasonCode,
                ReplayRollbackStatus.NotRequired,
                pending.PriorFailureReason,
                pending.PriorException
            );
            return;
        }

        if (Time.realtimeSinceStartup < pending.DeadlineRealtimeSeconds)
            return;

        _pendingMenuReturn = null;
        CompletePlaybackOperation(
            pending.Operation,
            pending.EndReasonCode,
            ReplayRollbackStatus.NotRequired,
            ReplayPlaybackReasonCode.MenuReturnFailed,
            new TimeoutException("Replay menu return was not confirmed before the deadline.")
        );
    }

    private static ReplayMenuReturnOutcome TryBeginReturnToMainMenu()
    {
        try
        {
            var runManager = Services.Get<RunManager>();
            if (runManager == null)
            {
                return ReplayMenuReturnOutcome.Failure(
                    new InvalidOperationException("RunManager is unavailable.")
                );
            }

            runManager.ReturnToMainMenu();
            return ReplayMenuReturnOutcome.Success();
        }
        catch (Exception ex)
        {
            return ReplayMenuReturnOutcome.Failure(ex);
        }
    }

    private void CompletePlaybackOperation(
        ReplayPlaybackLogOperation operation,
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayRollbackStatus rollbackStatus,
        ReplayPlaybackReasonCode failureReasonCode,
        Exception? exception
    )
    {
        if (
            operation.TryComplete(
                endReasonCode,
                rollbackStatus,
                failureReasonCode,
                exception,
                out var terminal
            )
        )
        {
            ReplayPlaybackLogWriter.EmitTerminal(terminal);
        }

        if (ReferenceEquals(_activePlaybackOperation, operation))
            _activePlaybackOperation = null;
    }

    private static void LogRequestRejected(
        CombatReplayPlaybackSource source,
        ReplayRequestRejectionReasonCode reasonCode,
        string? battleId
    )
    {
        BppLog.DebugEvent(
            CombatReplayLogEvents.RequestRejected,
            () =>
                [
                    CombatReplayLogEvents.RequestRejectedSource.Bind(source),
                    CombatReplayLogEvents.RequestRejectedReasonCode.Bind(reasonCode),
                    CombatReplayLogEvents.RequestRejectedBattleId.Bind(battleId),
                ]
        );
    }

    private ReplayRequestRejectionReasonCode ResolveSavedReplayRejectionReason(string? battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return ReplayRequestRejectionReasonCode.InvalidBattleId;
        if (_controller == null)
            return ReplayRequestRejectionReasonCode.RuntimeUnavailable;
        var general = ResolveGeneralReplayRejectionReason();
        if (general != ReplayRequestRejectionReasonCode.RuntimeUnavailable)
            return general;
        return !_controller.HasSavedReplay(battleId)
            ? ReplayRequestRejectionReasonCode.PayloadUnavailable
            : ReplayRequestRejectionReasonCode.RuntimeUnavailable;
    }

    private ReplayRequestRejectionReasonCode ResolveGeneralReplayRejectionReason()
    {
        if (IsReplayStartInProgress)
            return ReplayRequestRejectionReasonCode.ReplayAlreadyStarting;
        if (_services?.RunContext.IsInGameRun == true)
            return ReplayRequestRejectionReasonCode.ActiveRun;
        if (AppState.CurrentState is ReplayState)
            return ReplayRequestRejectionReasonCode.ReplayAlreadyActive;
        return ReplayRequestRejectionReasonCode.RuntimeUnavailable;
    }

    // Patches/Combat/CombatReplayVisualPatches.cs calls this static facade — keep the surface.
    public static void HideEncounterPickerOverlays() =>
        HealthBarBinder.HideEncounterPickerOverlays();
}

internal readonly record struct ReplayMenuReturnOutcome(bool Succeeded, Exception? Exception)
{
    internal static ReplayMenuReturnOutcome Success() => new(true, null);

    internal static ReplayMenuReturnOutcome Failure(Exception exception) =>
        new(false, exception ?? throw new ArgumentNullException(nameof(exception)));
}

internal sealed record PendingReplayMenuReturn(
    ReplayPlaybackLogOperation Operation,
    ReplayPlaybackEndReasonCode EndReasonCode,
    ReplayPlaybackReasonCode PriorFailureReason,
    Exception? PriorException,
    float DeadlineRealtimeSeconds
);

internal sealed class ReplayPlaybackStartInterruptedException : Exception
{
    internal ReplayPlaybackStartInterruptedException(
        ReplayPlaybackReasonCode reasonCode,
        Exception? innerException
    )
        : base("ReplayState exited before replay startup completed.", innerException)
    {
        ReasonCode = reasonCode;
    }

    internal ReplayPlaybackReasonCode ReasonCode { get; }
}
