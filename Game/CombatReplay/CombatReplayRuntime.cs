#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.CombatReplay.Warmup;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayRuntime : MonoBehaviour
{
    private IBppServices? _services;
    private RunLifecycleModule? _runLifecycle;
    private CombatReplayCaptureService? _captureService;
    private CombatReplayLoader? _loader;
    private CombatReplayController? _controller;
    private ReplayPersistenceOrchestrator? _persistence;
    private ReplayPlaybackPublisher? _playbackPublisher;
    private OpponentPortraitController? _portraitController;

    private bool _returnToMenuAfterReplay;
    private bool _bootstrappedReplayActive;
    private bool _isReplayStartInProgress;
    private bool _savedReplayPlaybackActive;

    public static CombatReplayRuntime? Instance { get; private set; }

    public string? ActiveBattleId => _controller?.ActiveBattleId;

    public bool IsReplayPlaybackActive =>
        _savedReplayPlaybackActive || AppState.CurrentState is ReplayState;

    public bool IsSavedReplayPlaybackActive => _savedReplayPlaybackActive;

    public bool IsReplayStartInProgress => _isReplayStartInProgress;

    public bool HasPendingPersistence => _persistence?.HasPendingPersistence == true;

    private void Awake()
    {
        Instance = this;
    }

    public void Initialize(IBppServices services, RunLifecycleModule runLifecycle)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _runLifecycle = runLifecycle ?? throw new ArgumentNullException(nameof(runLifecycle));

        _persistence = new ReplayPersistenceOrchestrator(_services);
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
    }

    private void Update()
    {
        _persistence?.DrainPendingResults();
    }

    private void OnDestroy()
    {
        _persistence?.Dispose();

        if (Instance == this)
            Instance = null;

        Events.StateChanged.RemoveListener(OnStateChanged);
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
        if (_isReplayStartInProgress)
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

            _persistence.Enqueue(artifact.Payload, artifact.Manifest);
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatReplayRuntime", $"Failed to capture combat replay: {ex}");
        }
    }

    public bool ReplayLatest()
    {
        var latest = _controller?.GetLatestBattle();
        if (latest == null)
            return false;

        return ReplaySaved(latest.BattleId);
    }

    public bool ReplaySaved(string battleId)
    {
        if (!CanReplaySavedBattle(battleId, out var reason))
        {
            BppLog.Warn("CombatReplayRuntime", $"Rejected saved replay request: {reason}");
            return false;
        }

        var controller = _controller;
        if (controller == null)
            return false;

        var manifest = controller.LoadBattle(battleId);
        if (manifest == null)
            return false;

        var payload = controller.LoadPayload(manifest);
        if (payload == null)
            return false;

        var sequence = controller.LoadReplay(payload);
        PlaybackUiState.InitializedBoardUiControllers.Clear();
        _savedReplayPlaybackActive = true;
        _ = StartReplayAsync(manifest, sequence, battleId, CombatReplayPlaybackSource.LocalSaved);
        return true;
    }

    public bool ReplayImportedBattle(PvpBattleManifest manifest, PvpReplayPayload payload)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        if (!CanReplaySavedCombats(out var reason))
        {
            BppLog.Warn("CombatReplayRuntime", $"Rejected imported replay request: {reason}");
            return false;
        }

        var loader = _loader;
        if (loader == null)
            return false;

        var sequence = loader.Load(payload);
        PlaybackUiState.InitializedBoardUiControllers.Clear();
        _savedReplayPlaybackActive = true;
        _ = StartReplayAsync(
            manifest,
            sequence,
            manifest.BattleId,
            CombatReplayPlaybackSource.ImportedGhost
        );
        return true;
    }

    private async Task StartReplayAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        string battleId,
        CombatReplayPlaybackSource source
    )
    {
        var attemptedBootstrapFromLobby = false;
        _isReplayStartInProgress = true;
        _playbackPublisher!.BeginSession(battleId, manifest, source);
        try
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            _portraitController!.Cleanup();
            _portraitController.ApplySelectedHeroOverride(manifest);
            Data.ResetRunData();
            _runLifecycle!.RefreshRunStateFromCurrentState();
            attemptedBootstrapFromLobby = !ReplayBootstrap.IsBootstrapReady();
            var bootstrappedFromLobby = await ReplayBootstrap.EnsureBootstrapReadyAsync();
            _returnToMenuAfterReplay = bootstrappedFromLobby;
            var bootstrapContext = ReplayBootstrap.ResolveDependencies();
            OpponentPortraitController.EnsureOpponentIdentity(manifest, sequence.SpawnMessage);
            await _portraitController.EnsureTemporaryOpponentPortraitAsync(manifest);
            await ReplayBootstrap.InjectSavedReplayAsync(
                bootstrapContext,
                manifest,
                sequence,
                battleId,
                _playbackPublisher.PublishStarting
            );
            _bootstrappedReplayActive = bootstrappedFromLobby;
            BppLog.Info("CombatReplayRuntime", $"Started replay for saved combat {battleId}");
        }
        catch (Exception ex)
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            _savedReplayPlaybackActive = false;
            _portraitController!.Cleanup();
            _portraitController.RestoreSelectedHeroOverride();
            BppLog.Error("CombatReplayRuntime", $"Failed to start replay {battleId}: {ex}");
            if (_playbackPublisher!.StartingPublished)
            {
                _playbackPublisher.PublishEnded("start-failed", failed: true);
            }
            if (attemptedBootstrapFromLobby)
                await ReplayBootstrap.RollbackBootstrapAsync();
        }
        finally
        {
            _isReplayStartInProgress = false;
        }
    }

    private void OnStateChanged(StateChangedEvent data)
    {
        if (data == null)
            return;

        if (data.PreviousState is not ReplayState || data.CurrentState is ReplayState)
            return;

        _portraitController?.RestoreSelectedHeroOverride();
        _savedReplayPlaybackActive = false;
        _playbackPublisher?.PublishEnded("state-exit", failed: false);
        _portraitController?.Cleanup();
        PlaybackUiState.InitializedBoardUiControllers.Clear();

        if (!_returnToMenuAfterReplay || !_bootstrappedReplayActive)
            return;

        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;

        try
        {
            BppLog.Info(
                "CombatReplayRuntime",
                "Returning to main menu after bootstrapped replay exit."
            );
            Services.Get<RunManager>()?.ReturnToMainMenu();
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CombatReplayRuntime",
                $"Failed to return to main menu after replay: {ex}"
            );
        }
    }

    internal static bool TryExitBootstrappedSavedReplayToMenu()
    {
        var instance = Instance;
        if (
            instance == null
            || !instance._savedReplayPlaybackActive
            || !instance._bootstrappedReplayActive
        )
        {
            return false;
        }

        instance.ExitBootstrappedSavedReplayToMenu();
        return true;
    }

    private void ExitBootstrappedSavedReplayToMenu()
    {
        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;
        _savedReplayPlaybackActive = false;
        _isReplayStartInProgress = false;
        _portraitController?.RestoreSelectedHeroOverride();
        _portraitController?.Cleanup();
        PlaybackUiState.InitializedBoardUiControllers.Clear();

        try
        {
            BppLog.Info("CombatReplayRuntime", "Returning to main menu after saved replay exit.");
            Services.Get<RunManager>()?.ReturnToMainMenu();
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatReplayRuntime", $"Failed to exit saved replay: {ex}");
        }
    }

    // Patches/Combat/CombatReplayVisualPatches.cs calls this static facade — keep the surface.
    public static void HideEncounterPickerOverlays() =>
        HealthBarBinder.HideEncounterPickerOverlays();

    // Patches/Combat/ReplayStateAudioDiagnosticPatch.cs calls this static facade — keep the surface.
    public static void LogReplayAudioState(string label) =>
        AudioBankWarmer.LogAudioState(label);
}
