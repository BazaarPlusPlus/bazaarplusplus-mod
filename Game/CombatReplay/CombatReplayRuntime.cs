#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
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
    private bool _savedReplayPlaybackActive;

    public static CombatReplayRuntime? Instance { get; private set; }

    public string? ActiveReplayId => _controller?.ActiveReplayId;

    public bool IsReplayPlaybackActive => _savedReplayPlaybackActive || AppState.CurrentState is ReplayState;

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

        if (ModState.IsInGameRun)
        {
            reason = "Saved replay playback is only available while you are outside an active gameplay session.";
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

        var record = _controller.LoadReplayRecord(replayId);
        if (record == null)
            return false;

        var sequence = _controller.LoadReplay(record);
        _savedReplayPlaybackActive = true;
        _ = StartReplayAsync(record, sequence, replayId);
        return true;
    }

    private async Task StartReplayAsync(
        CombatReplayRecord record,
        CombatSequenceMessages sequence,
        string replayId
    )
    {
        var attemptedBootstrapFromLobby = false;
        _isReplayStartInProgress = true;
        try
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            Data.ResetRunData();
            ModState.RefreshRunStateFromCurrentState();
            attemptedBootstrapFromLobby = !IsReplayBootstrapReady();
            var bootstrappedFromLobby = await EnsureReplayBootstrapReadyAsync();
            _returnToMenuAfterReplay = bootstrappedFromLobby;
            var bootstrapContext = ResolveReplayDependencies();
            await TryInjectSavedReplayAsync(bootstrapContext, record, sequence, replayId);
            _bootstrappedReplayActive = bootstrappedFromLobby;
            BppLog.Info("CombatReplayRuntime", $"Started replay for saved combat {replayId}");
        }
        catch (Exception ex)
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            _savedReplayPlaybackActive = false;
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
        EnsureReplayAppStateHandlersInitialized();
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
        var socketBehavior = EnsureSocketBehavior();
        var processor = GetProcessor(socketBehavior);
        EnsureReplayAppStateHandlersInitialized(processor);

        var gameSimHandler = GetGameSimHandler();
        var bootstrapContext = new ReplayBootstrapContext(
            socketBehavior,
            processor,
            gameSimHandler,
            CreateSetLastCombatSequence(processor),
            CreateHandleSpawnMessageAsync(processor, gameSimHandler),
            CreateTriggerCombatSequenceCreated(processor)
        );
        BppLog.Info("CombatReplayRuntime", "Replay bootstrap dependencies resolved.");
        return bootstrapContext;
    }

    private static async Task TryInjectSavedReplayAsync(
        ReplayBootstrapContext bootstrapContext,
        CombatReplayRecord record,
        CombatSequenceMessages sequence,
        string replayId
    )
    {
        bootstrapContext.SetLastCombatSequence(sequence);
        await bootstrapContext.HandleSpawnMessageAsync(sequence.SpawnMessage);
        RehydrateSavedReplayPlayerCards(record, sequence.SpawnMessage);
        bootstrapContext.TriggerCombatSequenceCreated();
        await Task.Delay(50);
        await AppState.TryPushState<ReplayState>();
        if (AppState.CurrentState is not ReplayState replayState)
            throw new InvalidOperationException("ReplayState did not become active.");
        Singleton<BoardManager>.Instance.ToggleOpponentPortrait(isVisible: true);
        replayState.Replay();
        Singleton<BoardManager>.Instance.ShowReplayAndRecapButtons(show: false, deactivate: true);

        BppLog.Info("CombatReplayRuntime", $"Saved replay injection completed for {replayId}.");
    }

    private static void RehydrateSavedReplayPlayerCards(
        CombatReplayRecord record,
        NetMessageGameSim spawnMessage
    )
    {
        if (record.PlayerHandCards.Count == 0)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay {record.ReplayId} does not contain player-hand snapshots; player cards may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        foreach (var snapshot in record.PlayerHandCards.Where(snapshot => snapshot != null))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;

            var card = Data.GetOrCreateCard(snapshot.InstanceId, snapshot.TemplateId, snapshot.Type);
            if (spawnMessage.Data.Cards.TryGetValue(snapshot.InstanceId, out var simUpdate))
                card.Update(simUpdate);

            card.Size = snapshot.Size;
            card.Owner = Data.Run.Player;
            card.Section = snapshot.Section;
            card.LeftSocketId = snapshot.Socket;
        }
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
            && Singleton<GameServiceManager>.Instance.IsInitialized
            && TryGetAppStateField<GameSimHandler>("_gameSimHandler") != null;
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
        _savedReplayPlaybackActive = false;

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

    private static object EnsureSocketBehavior()
    {
        var socketBehavior = TryGetSocketBehavior();
        if (socketBehavior != null)
            return socketBehavior;

        throw new InvalidOperationException("SocketBehavior is unavailable.");
    }

    private static object? TryGetSocketBehavior()
    {
        try
        {
            var replayHostType = ResolveReplayHostType();
            if (replayHostType == null)
                return null;

            var getInstanceMethod = replayHostType.GetMethod(
                "GetInstance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (getInstanceMethod == null)
                return null;

            return getInstanceMethod.Invoke(null, null);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Failed to resolve SocketBehavior via runtime API: {ex.Message}"
            );
            return null;
        }
    }

    private static Type? ResolveReplayHostType()
    {
        return typeof(NetMessageProcessor).Assembly.GetType("Networking.NetworkManager", false)
            ?? typeof(NetMessageProcessor).Assembly.GetType("Networking.SocketBehavior", false)
            ?? FindType("Networking.NetworkManager")
            ?? FindType("Networking.SocketBehavior")
            ?? FindTypeByName("NetworkManager")
            ?? FindTypeByName("SocketBehavior");
    }

    private static void DisposeSocketBehavior()
    {
        try
        {
            var socketBehavior = TryGetSocketBehavior();
            if (socketBehavior == null)
                return;
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

    private static NetMessageProcessor GetProcessor(object? socketBehavior)
    {
        socketBehavior ??= EnsureSocketBehavior();

        var method = socketBehavior
            .GetType()
            .GetMethod(
                "GetProcessor",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        var processor = method?.Invoke(socketBehavior, null) as NetMessageProcessor;
        if (processor != null)
            return processor;

        throw new InvalidOperationException("SocketBehavior did not expose a NetMessageProcessor.");
    }

    private static void EnsureReplayAppStateHandlersInitialized(NetMessageProcessor? processor = null)
    {
        if (TryGetAppStateField<GameSimHandler>("_gameSimHandler") != null)
            return;

        processor ??= TryGetAppStateField<NetMessageProcessor>("_messageProcessor");
        processor ??= GetProcessor(TryGetSocketBehavior());

        var sharedVariables = TryGetAppStateField<SharedVariablesSO>("_sharedVariablesSo");
        if (sharedVariables == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<SharedVariablesSO>())
            {
                sharedVariables = candidate;
                break;
            }
        }

        if (sharedVariables == null)
            throw new InvalidOperationException("SharedVariablesSO is unavailable.");

        AppState.Initialize(sharedVariables, processor);
    }

    private static GameSimHandler GetGameSimHandler()
    {
        return TryGetAppStateField<GameSimHandler>("_gameSimHandler")
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
        return () =>
        {
            var field = processor
                .GetType()
                .GetField(
                    "CombatSequenceCreated",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            var action = field?.GetValue(processor) as Action;
            action?.Invoke();
        };
    }

    private static Func<NetMessageGameSim, Task> CreateHandleSpawnMessageAsync(
        NetMessageProcessor processor,
        GameSimHandler gameSimHandler
    )
    {
        return async spawnMessage =>
        {
            if (!processor.Handle(spawnMessage))
                throw new InvalidOperationException(
                    "NetMessageProcessor rejected the replay spawn message."
                );

            Data.UpdateFromGameSimAsync(spawnMessage);
            MarkGameSimMessageHandled(gameSimHandler, spawnMessage.MessageId);
            await Task.CompletedTask;
        };
    }

    private static void MarkGameSimMessageHandled(GameSimHandler gameSimHandler, string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return;

        var handledMessagesField = gameSimHandler
            .GetType()
            .BaseType?.GetField(
                "_handledMessages",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
        if (handledMessagesField?.GetValue(gameSimHandler) is not List<string> handledMessages)
            throw new MissingFieldException(
                gameSimHandler.GetType().BaseType?.FullName,
                "_handledMessages"
            );

        if (!handledMessages.Contains(messageId))
            handledMessages.Add(messageId);
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = assembly.GetType(fullName, throwOnError: false);
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    private static Type? FindTypeByName(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = Array.FindAll(ex.Types, type => type != null)!;
            }
            catch
            {
                continue;
            }

            foreach (var candidate in types)
            {
                if (candidate != null && string.Equals(candidate.Name, typeName, StringComparison.Ordinal))
                    return candidate;
            }
        }

        return null;
    }

    private static T? TryGetAppStateField<T>(string fieldName)
        where T : class
    {
        var field = typeof(AppState).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        return field?.GetValue(null) as T;
    }

    private sealed class ReplayBootstrapContext
    {
        public ReplayBootstrapContext(
            object? socketBehavior,
            NetMessageProcessor processor,
            GameSimHandler gameSimHandler,
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

        public object? SocketBehavior { get; }

        public NetMessageProcessor Processor { get; }

        public GameSimHandler GameSimHandler { get; }

        public Action<CombatSequenceMessages> SetLastCombatSequence { get; }

        public Func<NetMessageGameSim, Task> HandleSpawnMessageAsync { get; }

        public Action TriggerCombatSequenceCreated { get; }
    }
}
