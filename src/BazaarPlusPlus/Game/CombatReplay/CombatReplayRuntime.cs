#nullable enable
using System.Collections;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.GameInterop.Files;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayRuntime : MonoBehaviour
{
    private const float CurrentReplayRecapPostRollSeconds = 3f;

    private IBppServices? _services;
    private RunLifecycleModule? _runLifecycle;
    private CombatReplayCaptureService? _captureService;
    private CombatReplayLoader? _loader;
    private CombatReplayController? _controller;
    private ReplayPersistenceOrchestrator? _persistence;
    private ReplayPlaybackPublisher? _playbackPublisher;
    private OpponentPortraitController? _portraitController;
    private ReplayPlaybackLogOperation? _activePlaybackOperation;
    private ReplayPlaybackLogOperation? _pendingMenuReturnOperation;
    private readonly SavedReplayLifecycle _savedReplay = new();
    private Func<CombatReplayVideoRecorder?>? _videoRecorder;
    private readonly CurrentReplayRecordingState _currentRecording = new();
    private PvpBattleManifest? _currentRecordingManifest;
    private IDisposable? _recordingStartedSubscription;
    private IDisposable? _recordingCompletedSubscription;
    private Coroutine? _pendingCurrentReplayStart;
    private Coroutine? _pendingCurrentReplayRecapPostRoll;
    private Action? _invokeCurrentRecordingRecap;
    private bool _destroying;

    public static CombatReplayRuntime? Instance { get; private set; }

    // Sourced from the playback session (BeginSession sets it for both the local-saved and the
    // imported-ghost path); the controller only learns battle ids on the local-saved path.
    public string? ActiveBattleId => _playbackPublisher?.ActiveSessionBattleId;

    public bool IsReplayPlaybackActive =>
        IsSavedReplayPlaybackActive || AppState.CurrentState is ReplayState;

    public bool IsSavedReplayPlaybackActive => _savedReplay.IsSavedReplayPlaybackActive;

    public bool IsReplayStartInProgress => _savedReplay.IsReplayStartInProgress;

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
    }

    private void Update()
    {
        _persistence?.DrainPendingResults();

        ObservePendingMenuReturn();

        // The exit-in-progress latch clears itself once ReplayState is actually gone; this is
        // the only reliable signal on the bootstrapped exit path, where the state transition
        // happens via RunManager.ReturnToMainMenu without a normal ReplayState exit event.
        if (AppState.CurrentState is not ReplayState)
            _savedReplay.ObserveReplayStateGone();
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
        _pendingMenuReturnOperation = null;
        _savedReplay.ClearPendingMenuReturn();
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

        _playbackPublisher!.BeginSession(
            manifest.BattleId,
            manifest,
            CombatReplayPlaybackSource.CurrentNative,
            recordVideo: true
        );
        _invokeCurrentRecordingRecap = invokeNativeRecap;

        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager is { } && (boardManager.IsRecapViewOpen || boardManager.StorageMoving))
        {
            try
            {
                _pendingCurrentReplayStart = StartCoroutine(
                    StartCurrentReplayAfterRecapClosed(
                        recordingId,
                        invokeNativeReplay,
                        invokeNativeRecapBack
                    )
                );
                reason = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                FailCurrentReplayStart(
                    recordingId,
                    "native-recap-transition-start-failed",
                    ex.Message
                );
                reason = ex.Message;
                return false;
            }
        }

        return TryInvokeCurrentReplay(recordingId, invokeNativeReplay, out reason);
    }

    private IEnumerator StartCurrentReplayAfterRecapClosed(
        string recordingId,
        Action invokeNativeReplay,
        Action invokeNativeRecapBack
    )
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

            if (boardManager.IsRecapViewOpen && !boardManager.StorageMoving && !AppState.BlockInput)
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
            return;

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
    }

    private void OnVideoRecordingCompleted(CombatReplayVideoRecordingCompleted completed)
    {
        _currentRecording.ApplyCompletion(completed);
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
        _savedReplay.OnStartBegun();
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
        _savedReplay.OnStartBegun();
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
    /// <c>ReplayState.Exit()</c>). This is the only programmatic path allowed to exit ReplayState —
    /// finalizing any in-flight video recording depends on it.
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

        var now = Time.realtimeSinceStartup;
        if (_savedReplay.IsExitSuppressed(now))
        {
            reason = "Replay exit is already in progress.";
            return false;
        }

        // Mirror the native continue click: clear the LevelUp recap overlay first
        // (BoardManager.OnBoardRecapReplayButtonsContinueClicked guards on ERunState.LevelUp),
        // then Exit(). For bootstrapped saved replays the Exit() prefix patch reroutes into
        // TryExitBootstrappedSavedReplayToMenu, which publishes the recorder's "ended" signal.
        if (Data.CurrentState?.StateName == BazaarGameShared.Domain.Runs.ERunState.LevelUp)
            Singleton<BoardManager>.Instance?.ExitRecapReplayState();

        replay.Exit();
        _savedReplay.NoteProgrammaticExitLatched(now);
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
        _playbackPublisher!.BeginSession(battleId, manifest, source, recordVideo);
        try
        {
            ReplayOpeningStateRestorer.Cleanup();
            _portraitController!.Cleanup(battleId);
            _portraitController.ApplySelectedHeroOverride(manifest);
            Data.ResetRunData();
            _runLifecycle!.RefreshRunStateFromCurrentState();
            attemptedBootstrapFromLobby = !ReplayBootstrap.IsBootstrapReady();
            var bootstrappedFromLobby = await ReplayBootstrap.EnsureBootstrapReadyAsync();
            _savedReplay.OnBootstrapResolved(bootstrappedFromLobby);
            var bootstrapContext = ReplayBootstrap.ResolveDependencies(operation);
            try
            {
                OpponentPortraitController.EnsureOpponentIdentity(
                    manifest,
                    sequence.SpawnMessage,
                    operation
                );
                await _portraitController.EnsureTemporaryOpponentPortraitAsync(manifest, operation);
            }
            catch (Exception ex)
            {
                operation.ReportDegradation(
                    ReplayPlaybackReasonCode.OpponentPortraitUnavailable,
                    ex
                );
            }
            ReplayRunEconomyFallback.ApplyMissingRunEconomy(
                manifest,
                _services?.Paths.RunLogDatabasePath,
                operation
            );
            await ReplayBootstrap.InjectSavedReplayAsync(
                bootstrapContext,
                manifest,
                sequence,
                operation,
                _playbackPublisher.PublishStarting
            );
            var interruption = _savedReplay.TakeStartupInterruption();
            if (interruption != null)
            {
                throw new ReplayPlaybackStartInterruptedException(
                    interruption.Value.ReasonCode,
                    interruption.Value.Exception
                );
            }
            _savedReplay.OnInjectionCommitted();
            if (operation.TryMarkStarted(out var started))
                ReplayPlaybackLogWriter.EmitStarted(started);
        }
        catch (Exception ex)
        {
            _savedReplay.OnStartFailed();
            // Unconditional: PublishEnded only publishes the event when "starting" was
            // published, but it must always clear the session (battle id) for a failed start.
            // Cleanup order is explicit on this path (ADR-0009) — not shared with state-exit.
            var ended = ReplayPlaybackCleanup.PublishThenCleanup(
                () => _playbackPublisher!.PublishEnded("start-failed", failed: true),
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
            _ = _savedReplay.OnStartFinished();
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

        var now = Time.realtimeSinceStartup;
        var ownership = _savedReplay.BeginReplayStateExit(now);
        var operation = _activePlaybackOperation;
        // Cleanup order is explicit on this path (ADR-0009) — not shared with start-failure.
        var ended = ReplayPlaybackCleanup.PublishThenCleanup(
            () =>
                _playbackPublisher?.PublishEnded(
                    "state-exit",
                    failed: ownership.OwnsTerminalByStart
                ) ?? ReplayPlaybackPublishOutcome.Success(),
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

        var decision = _savedReplay.OnReplayStateExited(now, ended.Succeeded, ended.Exception);
        if (decision.Kind == SavedReplayStateExitKind.Defer)
            return;

        if (operation == null)
            return;

        if (decision.Kind == SavedReplayStateExitKind.CompleteNow)
        {
            CompletePlaybackOperation(
                operation,
                decision.EndReasonCode,
                ReplayRollbackStatus.NotRequired,
                decision.FailureReason,
                decision.Exception
            );
            return;
        }

        BeginPendingMenuReturn(
            operation,
            decision.EndReasonCode,
            decision.FailureReason,
            decision.Exception
        );
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

    internal static bool TryExitBootstrappedSavedReplayToMenu()
    {
        var instance = Instance;
        if (instance == null)
            return false;

        var now = Time.realtimeSinceStartup;
        var inReplayState = AppState.CurrentState is ReplayState;
        var decision = instance._savedReplay.RequestBootstrappedExit(now, inReplayState);
        return decision switch
        {
            // Suppressed: report "handled" so the Exit() prefix patch suppresses the original body.
            SavedReplayExitRequestDecision.Suppressed => true,
            SavedReplayExitRequestDecision.NotActive => false,
            SavedReplayExitRequestDecision.Proceed => instance.ExitBootstrappedSavedReplayToMenu(
                now
            ),
            _ => false,
        };
    }

    private bool ExitBootstrappedSavedReplayToMenu(float now)
    {
        // Bootstrapped saved replays exit through this manual path (the state-exit patch
        // intercepts the normal transition), so OnStateChanged's PublishEnded never fires for
        // them. Emit it here too, otherwise the video recorder never gets the "ended" signal and
        // leaves ffmpeg running on a never-finalized file (no moov atom -> unplayable MP4).
        // Cleanup order is explicit on this path (ADR-0009) — not shared with start-failure.
        var operation = _activePlaybackOperation;
        var ended = ReplayPlaybackCleanup.PublishThenCleanup(
            () =>
                _playbackPublisher?.PublishEnded("saved-replay-exit", failed: false)
                ?? ReplayPlaybackPublishOutcome.Success(),
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

        var decision = _savedReplay.OnReplayStateExited(now, ended.Succeeded, ended.Exception);
        if (operation != null && decision.Kind == SavedReplayStateExitKind.BeginMenuReturn)
        {
            BeginPendingMenuReturn(
                operation,
                decision.EndReasonCode,
                decision.FailureReason,
                decision.Exception
            );
        }

        return true;
    }

    private void BeginPendingMenuReturn(
        ReplayPlaybackLogOperation operation,
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayPlaybackReasonCode priorFailureReason,
        Exception? priorException
    )
    {
        // Lifecycle already armed the pending window when it emitted BeginMenuReturn. A sync
        // dispatch failure must clear that window (OnMenuReturnDispatchFailed) so TickMenuReturn
        // cannot later emit CompleteTimeout for an operation we complete here.
        var dispatch = TryBeginReturnToMainMenu();
        if (!dispatch.Succeeded)
        {
            _savedReplay.OnMenuReturnDispatchFailed();
            _pendingMenuReturnOperation = null;
            CompletePlaybackOperation(
                operation,
                endReasonCode,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.MenuReturnFailed,
                dispatch.Exception
            );
            return;
        }

        _pendingMenuReturnOperation = operation;
    }

    private void ObservePendingMenuReturn()
    {
        var operation = _pendingMenuReturnOperation;
        var decision = _savedReplay.TickMenuReturn(
            Time.realtimeSinceStartup,
            SceneLoader.IsSceneLoaded(SceneID.HeroSelectScene)
        );

        if (decision.Kind is SavedReplayMenuReturnKind.None or SavedReplayMenuReturnKind.Wait)
            return;

        _pendingMenuReturnOperation = null;
        if (operation == null)
            return;

        CompletePlaybackOperation(
            operation,
            decision.EndReasonCode,
            ReplayRollbackStatus.NotRequired,
            decision.FailureReason,
            decision.Exception
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
