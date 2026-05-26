#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Enums;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay;

internal static class ReplayBootstrapCoordinator
{
    public static async Task<bool> EnsureBootstrapReadyAsync()
    {
        if (IsBootstrapReady())
            return false;

        BppLog.Info("ReplayBootstrapCoordinator", "Bootstrapping gameplay scene for lobby replay.");
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
        await BootstrapManagersAsync();
        EnsureAppStateHandlersInitialized();
        await WaitUntilAsync(IsBootstrapReady, timeout: TimeSpan.FromSeconds(20));

        await SceneLoader.SetActiveScene(SceneID.GameScene);
        SceneLoader.LoadingComplete();
        if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
            await SceneLoader.UnloadScene(SceneID.GameplayLoading);

        BppLog.Info("ReplayBootstrapCoordinator", "Replay bootstrap scene environment is ready.");
        return true;
    }

    public static ReplayBootstrapContext ResolveDependencies()
    {
        var socketBehavior = EnsureSocketBehavior();
        var processor = GetProcessor(socketBehavior);
        EnsureAppStateHandlersInitialized(processor);

        var gameSimHandler = GetGameSimHandler();
        var bootstrapContext = new ReplayBootstrapContext(
            socketBehavior,
            processor,
            gameSimHandler,
            CreateSetLastCombatSequence(processor),
            CreateHandleSpawnMessageAsync(processor, gameSimHandler),
            CreateTriggerCombatSequenceCreated(processor)
        );
        BppLog.Info("ReplayBootstrapCoordinator", "Replay bootstrap dependencies resolved.");
        return bootstrapContext;
    }

    public static async Task InjectSavedReplayAsync(
        ReplayBootstrapContext bootstrapContext,
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        string battleId,
        Action? onBeforeReplayPlayback = null
    )
    {
        EnsureSequencePlayerAttributes(sequence);
        bootstrapContext.SetLastCombatSequence(sequence);
        await bootstrapContext.HandleSpawnMessageAsync(sequence.SpawnMessage);
        EnsureRunPlayerAttributes();
        RehydratePlayerCards(manifest, sequence.SpawnMessage);
        RehydrateOpponentCards(manifest, sequence.SpawnMessage);
        RehydratePlayerSkills(manifest, sequence.SpawnMessage);
        RehydrateOpponentSkills(manifest, sequence.SpawnMessage);
        SanitizeSpawnEvents(sequence);
        await RebuildSkillPresentationAsync();
        bootstrapContext.TriggerCombatSequenceCreated();
        await Task.Delay(50);
        await AppState.TryPushState<ReplayState>();
        if (AppState.CurrentState is not ReplayState replayState)
            throw new InvalidOperationException("ReplayState did not become active.");
        Singleton<BoardManager>.Instance.ShowReplayAndRecapButtons(show: false, deactivate: true);
        ReplayHealthBarRebuilder.HideEncounterPickerOverlays();
        EnsureOpponentPortraitVisible();
        await ReplayHealthBarRebuilder.PrepareHealthBarsAsync();
        Singleton<BoardManager>.Instance.ToggleOpponentPortrait(isVisible: true);
        await WaitForPresentationReadyAsync();
        await WarmupCoordinator.WarmPresentationAssetsAsync(manifest, sequence);
        await WarmupCoordinator.WarmAudioBanksAsync();
        WarmupCoordinator.EnsureAudioReadyForPlayback();
        ReplayHealthBarRebuilder.HideEncounterPickerOverlays();
        EnsureOpponentPortraitVisible();
        ReplayHealthBarRebuilder.RefillOpponentHealthBar();
        onBeforeReplayPlayback?.Invoke();
        replayState.Replay();
        EnsureOpponentPortraitVisible();
        Singleton<BoardManager>.Instance.ShowReplayAndRecapButtons(show: false, deactivate: true);

        BppLog.Info("ReplayBootstrapCoordinator", $"Saved replay injection completed for {battleId}.");
    }

    public static async Task RollbackBootstrapAsync()
    {
        try
        {
            BppLog.Warn(
                "ReplayBootstrapCoordinator",
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
            BppLog.Error("ReplayBootstrapCoordinator", $"Failed to roll back replay bootstrap: {ex}");
        }
    }

    public static bool IsBootstrapReady()
    {
        return SceneLoader.IsSceneLoaded(SceneID.GameScene)
            && Singleton<BoardManager>.Instance != null
            && Singleton<BoardManager>.Instance.IsInitialized
            && Singleton<GameServiceManager>.Instance != null
            && Singleton<GameServiceManager>.Instance.IsInitialized
            && TryGetAppStateField<GameSimHandler>("_gameSimHandler") != null;
    }

    private static async Task BootstrapManagersAsync()
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

    private static void SanitizeSpawnEvents(CombatSequenceMessages sequence)
    {
        var events = sequence.SpawnMessage?.Data?.Events;
        if (events == null || events.Count == 0)
            return;

        var removedCount = events.RemoveAll(ShouldRemoveSpawnEvent);
        if (removedCount > 0)
        {
            BppLog.Info(
                "ReplayBootstrapCoordinator",
                $"Removed {removedCount} non-combat opponent spawn events from saved replay bootstrap."
            );
        }
    }

    private static bool ShouldRemoveSpawnEvent(IGameSimEvent gameSimEvent)
    {
        return gameSimEvent is GameSimEventCardSpawned
        {
            CombatantId: ECombatantId.Opponent,
            Section: not EInventorySection.Hand,
        };
    }

    private static void RehydratePlayerCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.PlayerHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "ReplayBootstrapCoordinator",
                $"Saved replay {manifest.BattleId} does not contain player-hand snapshots; player cards may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        RehydrateCards(capture.Items, spawnMessage, Data.Run?.Player);
    }

    private static void RehydrateOpponentCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.OpponentHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "ReplayBootstrapCoordinator",
                $"Saved replay {manifest.BattleId} does not contain opponent-hand snapshots; opponent cards may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        RehydrateCards(capture.Items, spawnMessage, Data.Run?.Opponent);
    }

    private static void RehydratePlayerSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.PlayerSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "ReplayBootstrapCoordinator",
                $"Saved replay {manifest.BattleId} does not contain player-skill snapshots; player skills may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        var skills = RehydrateSkillCards(capture.Items, spawnMessage, Data.Run?.Player);
        ReplaceSkillCollection(Data.Run?.Player, skills);
    }

    private static void RehydrateOpponentSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.OpponentSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "ReplayBootstrapCoordinator",
                $"Saved replay {manifest.BattleId} does not contain opponent-skill snapshots; opponent skills may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        var skills = RehydrateSkillCards(
            capture.Items,
            spawnMessage,
            Data.Run?.Opponent
        );
        ReplaceSkillCollection(Data.Run?.Opponent, skills);
    }

    private static void RehydrateCards(
        IEnumerable<CombatReplayCardSnapshot> snapshots,
        NetMessageGameSim spawnMessage,
        IPlayer? owner
    )
    {
        foreach (var snapshot in snapshots.Where(snapshot => snapshot != null))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;

            var card = Data.GetOrCreateCard(
                snapshot.InstanceId,
                snapshot.TemplateId,
                snapshot.Type
            );
            if (spawnMessage.Data.Cards.TryGetValue(snapshot.InstanceId, out var simUpdate))
                card.Update(simUpdate);

            ApplySnapshotFallback(card, snapshot);
            card.Size = snapshot.Size;
            card.Owner = owner;
            card.Section = snapshot.Section;
            card.LeftSocketId = snapshot.Socket;
        }
    }

    private static List<SkillCard> RehydrateSkillCards(
        IEnumerable<CombatReplayCardSnapshot> snapshots,
        NetMessageGameSim spawnMessage,
        IPlayer? owner
    )
    {
        var skills = new List<SkillCard>();
        foreach (var snapshot in snapshots.Where(snapshot => snapshot != null))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;

            var card = Data.GetOrCreateCard(
                snapshot.InstanceId,
                snapshot.TemplateId,
                snapshot.Type
            );
            if (spawnMessage.Data.Cards.TryGetValue(snapshot.InstanceId, out var simUpdate))
                card.Update(simUpdate);

            ApplySnapshotFallback(card, snapshot);
            card.Size = snapshot.Size;
            card.Owner = owner;
            card.Section = snapshot.Section;
            card.LeftSocketId = snapshot.Socket;

            if (card is SkillCard skillCard)
                skills.Add(skillCard);
        }

        return skills;
    }

    private static void ApplySnapshotFallback(Card card, CombatReplayCardSnapshot snapshot)
    {
        if (snapshot.Attributes != null && snapshot.Attributes.Count > 0)
        {
            foreach (var entry in snapshot.Attributes)
            {
                if (
                    Enum.TryParse<ECardAttributeType>(
                        entry.Key,
                        ignoreCase: false,
                        out var attributeType
                    )
                )
                    card.Attributes[attributeType] = entry.Value;
            }
        }

        if (snapshot.Tags != null && snapshot.Tags.Count > 0)
        {
            card.Tags = snapshot
                .Tags.Select(tag =>
                    Enum.TryParse<ECardTag>(tag, ignoreCase: false, out var parsedTag)
                        ? (ECardTag?)parsedTag
                        : null
                )
                .Where(tag => tag.HasValue)
                .Select(tag => tag!.Value)
                .ToHashSet();
        }

        if (
            !string.IsNullOrWhiteSpace(snapshot.Tier)
            && Enum.TryParse<ETier>(snapshot.Tier, ignoreCase: false, out var tier)
        )
            card.Tier = tier;

        if (
            card is ItemCard itemCard
            && !string.IsNullOrWhiteSpace(snapshot.Enchant)
            && Enum.TryParse<EEnchantmentType>(
                snapshot.Enchant,
                ignoreCase: false,
                out var enchantment
            )
        )
            itemCard.Enchantment = enchantment;
    }

    private static void ReplaceSkillCollection(object? combatant, IReadOnlyList<SkillCard> skills)
    {
        if (combatant == null)
            return;

        var skillsProperty = combatant
            .GetType()
            .GetProperty(
                "Skills",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (skillsProperty == null)
            return;

        if (skillsProperty.CanWrite)
        {
            skillsProperty.SetValue(combatant, skills.ToList());
            return;
        }

        if (skillsProperty.GetValue(combatant) is System.Collections.IList list)
        {
            list.Clear();
            foreach (var skill in skills)
                list.Add(skill);
        }
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

    private static async Task WaitForPresentationReadyAsync()
    {
        await WaitUntilAsync(
            () =>
            {
                var boardManager = Singleton<BoardManager>.Instance;
                if (boardManager == null || !boardManager.IsInitialized)
                    return false;

                var playerSkillPresentationReady =
                    Data.PlayerSkillPresentationManager == null
                    || !Data.PlayerSkillPresentationManager.IsUpdatingSkillBoard;
                var opponentSkillPresentationReady =
                    Data.OpponentSkillPresenationManager == null
                    || !Data.OpponentSkillPresenationManager.IsUpdatingSkillBoard;

                return !boardManager.StorageMoving
                    && !boardManager.IsUpdatingBoard
                    && !boardManager.IsUpdatingSkillBoard
                    && playerSkillPresentationReady
                    && opponentSkillPresentationReady
                    && !boardManager.isUpdatingPresentation
                    && !boardManager.IsCarpetUnrolling
                    && !boardManager.HasCardsToReveal();
            },
            timeout: TimeSpan.FromSeconds(5)
        );

        // Let one more frame pass so ReplayState.OnEnter fire-and-forget spawn work can settle
        // before combat sim playback starts.
        await Task.Delay(100);
    }

    private static async Task RebuildSkillPresentationAsync()
    {
        var playerSkills = Data.Run?.Player?.Skills?.Cast<Card>().ToList() ?? new List<Card>();
        var opponentSkills = Data.Run?.Opponent?.Skills?.Cast<Card>().ToList() ?? new List<Card>();

        if (Data.PlayerSkillPresentationManager != null)
            await Data.PlayerSkillPresentationManager.Initialize(playerSkills);

        if (Data.OpponentSkillPresenationManager != null)
            await Data.OpponentSkillPresenationManager.Initialize(opponentSkills);
    }

    private static void EnsureOpponentPortraitVisible() =>
        ReplayHealthBarRebuilder.EnsureOpponentPortraitVisible();

    private static void EnsureSequencePlayerAttributes(CombatSequenceMessages sequence) =>
        ReplayHealthBarRebuilder.EnsureSequencePlayerAttributes(sequence);

    private static void EnsureRunPlayerAttributes() =>
        ReplayHealthBarRebuilder.EnsureRunPlayerAttributes();

    internal static object EnsureSocketBehavior()
    {
        var socketBehavior = TryGetSocketBehavior();
        if (socketBehavior != null)
            return socketBehavior;

        throw new InvalidOperationException("SocketBehavior is unavailable.");
    }

    internal static object? TryGetSocketBehavior()
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
                "ReplayBootstrapCoordinator",
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
                "ReplayBootstrapCoordinator",
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

    private static void EnsureAppStateHandlersInitialized(
        NetMessageProcessor? processor = null
    )
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
        };
    }

    private static void MarkGameSimMessageHandled(GameSimHandler gameSimHandler, string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return;

        var handledMessagesField = gameSimHandler
            .GetType()
            .BaseType?.GetField("_handledMessages", BindingFlags.Instance | BindingFlags.NonPublic);
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
                if (
                    candidate != null
                    && string.Equals(candidate.Name, typeName, StringComparison.Ordinal)
                )
                    return candidate;
            }
        }

        return null;
    }

    internal static T? TryGetAppStateField<T>(string fieldName)
        where T : class
    {
        var field = typeof(AppState).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        return field?.GetValue(null) as T;
    }
}

internal sealed class ReplayBootstrapContext
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
