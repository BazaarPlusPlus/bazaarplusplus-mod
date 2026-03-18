#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Infra.Messages;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayRuntime : MonoBehaviour
{
    private CombatReplayStore? _store;
    private CombatReplayCaptureService? _captureService;
    private CombatReplayLoader? _loader;
    private CombatReplayController? _controller;
    private bool _returnToMenuAfterReplay;
    private bool _bootstrappedReplayActive;
    private bool _isReplayStartInProgress;

    public static CombatReplayRuntime? Instance { get; private set; }

    public string? ActiveReplayId => _controller?.ActiveReplayId;

    private void Awake()
    {
        Instance = this;
        _store = new CombatReplayStore(ModState.CombatReplayDirectoryPath);
        _captureService = new CombatReplayCaptureService();
        _loader = new CombatReplayLoader();
        _controller = new CombatReplayController(_store, _loader);
        Events.StateChanged.AddListener(OnStateChanged, this);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        Events.StateChanged.RemoveListener(OnStateChanged);
    }

    public IReadOnlyList<CombatReplayRecord> ListSavedReplays()
    {
        return _controller?.ListSavedReplays() ?? Array.Empty<CombatReplayRecord>();
    }

    public CombatReplayRecord? GetLatestReplay()
    {
        return _controller?.GetLatestReplay();
    }

    public bool CanReplaySavedCombats(out string reason)
    {
        if (_isReplayStartInProgress)
        {
            reason = "A saved replay is already starting.";
            return false;
        }

        if (Data.HasActiveRun)
        {
            reason = "Saved replay playback is only available from the lobby with no active run.";
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

    public void ObserveMessage(INetMessage message)
    {
        if (_captureService == null || _store == null)
            return;

        try
        {
            var record = _captureService.Accept(message, ModState.CurrentServerRunId);
            if (record == null)
                return;

            _store.Save(record);
            BppLog.Info(
                "CombatReplayRuntime",
                $"Saved combat replay {record.ReplayId} for run={record.RunId ?? "unknown"}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatReplayRuntime", $"Failed to capture combat replay: {ex}");
        }
    }

    public bool ReplayLatest()
    {
        var latest = _controller?.GetLatestReplay();
        if (latest == null)
            return false;

        return ReplaySaved(latest.ReplayId);
    }

    public bool ReplaySaved(string replayId)
    {
        if (_controller == null)
            return false;
        if (!CanReplaySavedCombats(out var reason))
        {
            BppLog.Warn("CombatReplayRuntime", $"Rejected saved replay request: {reason}");
            return false;
        }

        var sequence = _controller.LoadReplay(replayId);
        if (sequence == null)
            return false;

        _ = StartReplayAsync(sequence, replayId);
        return true;
    }

    private async Task StartReplayAsync(CombatSequenceMessages sequence, string replayId)
    {
        var attemptedBootstrapFromLobby = false;
        _isReplayStartInProgress = true;
        try
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            attemptedBootstrapFromLobby = !IsReplayBootstrapReady();
            var bootstrappedFromLobby = await EnsureReplayBootstrapReadyAsync();
            _returnToMenuAfterReplay = bootstrappedFromLobby;
            var bootstrapContext = ResolveReplayDependencies();
            await TryInjectSavedReplayAsync(bootstrapContext, sequence, replayId);
            _bootstrappedReplayActive = bootstrappedFromLobby;
            BppLog.Info("CombatReplayRuntime", $"Started replay for saved combat {replayId}");
        }
        catch (Exception ex)
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            BppLog.Error("CombatReplayRuntime", $"Failed to start replay {replayId}: {ex}");
            if (attemptedBootstrapFromLobby)
                await RollbackReplayBootstrapAsync();
        }
        finally
        {
            _isReplayStartInProgress = false;
        }
    }

    private static async Task<bool> EnsureReplayBootstrapReadyAsync()
    {
        if (IsReplayBootstrapReady())
            return false;

        BppLog.Info("CombatReplayRuntime", "Bootstrapping gameplay scene for lobby replay.");
        Data.ResetRunData();
        if (!SceneLoader.IsSceneLoaded(SceneID.GameScene))
        {
            await SceneLoader.LoadScene(
                SceneID.GameScene,
                shouldUnloadCurrentScene: true,
                showLoadingScene: false
            );
        }

        if (!SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
            await SceneLoader.LoadSceneAdditive(SceneID.GameplayLoading);

        await WaitUntilAsync(
            () => Singleton<GameServiceManager>.Instance != null,
            timeout: TimeSpan.FromSeconds(20)
        );
        await BootstrapReplayManagersAsync();
        await WaitUntilAsync(
            IsReplayBootstrapReady,
            timeout: TimeSpan.FromSeconds(20)
        );

        await SceneLoader.SetActiveScene(SceneID.GameScene);
        SceneLoader.LoadingComplete();
        if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
            await SceneLoader.UnloadScene(SceneID.GameplayLoading);

        BppLog.Info("CombatReplayRuntime", "Replay bootstrap scene environment is ready.");
        return true;
    }

    private static async Task BootstrapReplayManagersAsync()
    {
        var runManager = Services.Get<RunManager>();
        if (runManager == null)
            throw new InvalidOperationException("RunManager is unavailable.");

        var gameServiceManager = Singleton<GameServiceManager>.Instance;
        if (gameServiceManager == null)
            throw new InvalidOperationException("GameServiceManager is unavailable.");

        if (Singleton<BoardManager>.Instance != null && gameServiceManager.IsInitialized)
            return;

        var boardReferenceField = typeof(RunManager).GetField(
            "_baseBoardReference",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        var boardReference =
            boardReferenceField?.GetValue(runManager) as AssetReference
            ?? throw new MissingFieldException(typeof(RunManager).FullName, "_baseBoardReference");

        var boardBuilder = new BoardBuilder();
        runManager.BoardBuilder = boardBuilder;
        var boardManager = await boardBuilder.SetUpBoard(boardReference);
        await gameServiceManager.Init(boardManager);
    }

    private static ReplayBootstrapContext ResolveReplayDependencies()
    {
        var socketBehavior = GetSocketBehavior();
        var processor = GetProcessor(socketBehavior);
        if (processor == null)
            throw new InvalidOperationException("NetMessageProcessor is unavailable.");

        var gameSimHandler = GetGameSimHandler();
        var bootstrapContext = new ReplayBootstrapContext(
            socketBehavior,
            processor,
            gameSimHandler,
            CreateSetLastCombatSequence(processor),
            CreateHandleSpawnMessageAsync(gameSimHandler),
            CreateTriggerCombatSequenceCreated(processor)
        );
        BppLog.Info("CombatReplayRuntime", "Replay bootstrap dependencies resolved.");
        return bootstrapContext;
    }

    private static async Task TryInjectSavedReplayAsync(
        ReplayBootstrapContext bootstrapContext,
        CombatSequenceMessages sequence,
        string replayId
    )
    {
        bootstrapContext.SetLastCombatSequence(sequence);
        await bootstrapContext.HandleSpawnMessageAsync(sequence.SpawnMessage);
        bootstrapContext.TriggerCombatSequenceCreated();
        await Task.Delay(50);
        await AppState.TryPushState<ReplayState>();
        if (AppState.CurrentState is not ReplayState)
            throw new InvalidOperationException("ReplayState did not become active.");

        BppLog.Info("CombatReplayRuntime", $"Saved replay injection completed for {replayId}.");
    }

    private static async Task RollbackReplayBootstrapAsync()
    {
        try
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                "Replay bootstrap failed. Resetting replay state and returning to lobby."
            );
            AppState.Reset();
            Data.ResetRunData();
            DisposeSocketBehavior();

            if (Singleton<GameServiceManager>.Instance != null)
                Singleton<GameServiceManager>.Instance.PauseOrUnpauseGame(toPauseOrUnpause: false);

            if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
                await SceneLoader.UnloadScene(SceneID.GameplayLoading);

            await SceneLoader.LoadScene(
                SceneID.HeroSelectScene,
                shouldUnloadCurrentScene: true,
                showLoadingScene: false
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatReplayRuntime", $"Failed to roll back replay bootstrap: {ex}");
        }
    }

    private static bool IsReplayBootstrapReady()
    {
        return SceneLoader.IsSceneLoaded(SceneID.GameScene)
            && Singleton<BoardManager>.Instance != null
            && Singleton<BoardManager>.Instance.IsInitialized
            && Singleton<GameServiceManager>.Instance != null
            && Singleton<GameServiceManager>.Instance.IsInitialized;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Timed out while bootstrapping replay environment.");

            await Task.Delay(100);
        }
    }

    private void OnStateChanged(StateChangedEvent data)
    {
        if (!_returnToMenuAfterReplay || !_bootstrappedReplayActive || data == null)
            return;

        if (data.PreviousState is not ReplayState || data.CurrentState is ReplayState)
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

    private static object GetSocketBehavior()
    {
        var socketBehaviorType =
            Type.GetType("Networking.SocketBehavior, TheBazaarRuntime")
            ?? Type.GetType("Networking.SocketBehavior, Assembly-CSharp");
        if (socketBehaviorType == null)
            throw new InvalidOperationException("SocketBehavior type is unavailable.");

        var getInstanceMethod = socketBehaviorType.GetMethod(
            "GetInstance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        if (getInstanceMethod == null)
            throw new MissingMethodException(socketBehaviorType.FullName, "GetInstance");

        return getInstanceMethod.Invoke(null, null)
            ?? throw new InvalidOperationException("SocketBehavior instance is unavailable.");
    }

    private static void DisposeSocketBehavior()
    {
        try
        {
            var socketBehavior = GetSocketBehavior();
            var disposeMethod = socketBehavior
                .GetType()
                .GetMethod(
                    "Dispose",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            disposeMethod?.Invoke(socketBehavior, null);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Failed to dispose socket during replay rollback: {ex.Message}"
            );
        }
    }

    private static object? GetProcessor(object socketBehavior)
    {
        var method = socketBehavior
            .GetType()
            .GetMethod(
                "GetProcessor",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        return method?.Invoke(socketBehavior, null);
    }

    private static object GetGameSimHandler()
    {
        var field = typeof(AppState).GetField(
            "_gameSimHandler",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        return field?.GetValue(null)
            ?? throw new InvalidOperationException("GameSimHandler is unavailable.");
    }

    private static Action<CombatSequenceMessages> CreateSetLastCombatSequence(object processor)
    {
        var property = processor
            .GetType()
            .GetProperty(
                "LastCombatSequence",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (property == null)
            throw new MissingMemberException(processor.GetType().FullName, "LastCombatSequence");

        return sequence => property.SetValue(processor, sequence);
    }

    private static Action CreateTriggerCombatSequenceCreated(object processor)
    {
        var field = processor
            .GetType()
            .GetField(
                "CombatSequenceCreated",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        var action = field?.GetValue(processor) as Action;
        return () => action?.Invoke();
    }

    private static Func<NetMessageGameSim, Task> CreateHandleSpawnMessageAsync(object gameSimHandler)
    {
        var method = gameSimHandler
            .GetType()
            .GetMethod(
                "Handle",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (method == null)
            throw new MissingMethodException(gameSimHandler.GetType().FullName, "Handle");

        return spawnMessage => (Task)method.Invoke(gameSimHandler, new object[] { spawnMessage })!;
    }

    private sealed class ReplayBootstrapContext
    {
        public ReplayBootstrapContext(
            object socketBehavior,
            object processor,
            object gameSimHandler,
            Action<CombatSequenceMessages> setLastCombatSequence,
            Func<NetMessageGameSim, Task> handleSpawnMessageAsync,
            Action triggerCombatSequenceCreated
        )
        {
            SocketBehavior = socketBehavior;
            Processor = processor;
            GameSimHandler = gameSimHandler;
            SetLastCombatSequence = setLastCombatSequence;
            HandleSpawnMessageAsync = handleSpawnMessageAsync;
            TriggerCombatSequenceCreated = triggerCombatSequenceCreated;
        }

        public object SocketBehavior { get; }

        public object Processor { get; }

        public object GameSimHandler { get; }

        public Action<CombatSequenceMessages> SetLastCombatSequence { get; }

        public Func<NetMessageGameSim, Task> HandleSpawnMessageAsync { get; }

        public Action TriggerCombatSequenceCreated { get; }
    }
}
