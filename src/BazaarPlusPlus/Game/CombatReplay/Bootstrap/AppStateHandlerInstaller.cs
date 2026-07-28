#nullable enable

using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class AppStateHandlerInstaller
{
    internal static void EnsureAppStateHandlersInitialized(NetMessageProcessor? processor = null)
    {
        if (ReplayBootstrap.TryGetAppStateField<GameSimHandler>("_gameSimHandler") != null)
            return;

        processor ??= ReplayBootstrap.TryGetAppStateField<NetMessageProcessor>("_messageProcessor");
        processor ??= SocketBehaviorBridge.GetProcessor(
            SocketBehaviorBridge.TryGetSocketBehavior()
        );

        var sharedVariables = ReplayBootstrap.TryGetAppStateField<SharedVariablesSO>(
            "_sharedVariablesSo"
        );
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

    internal static GameSimHandler GetGameSimHandler()
    {
        return ReplayBootstrap.TryGetAppStateField<GameSimHandler>("_gameSimHandler")
            ?? throw new InvalidOperationException("GameSimHandler is unavailable.");
    }

    internal static async Task RebuildSkillPresentationAsync()
    {
        var playerSkills = Data.Run?.Player?.Skills?.Cast<Card>().ToList() ?? new List<Card>();
        var opponentSkills = Data.Run?.Opponent?.Skills?.Cast<Card>().ToList() ?? new List<Card>();

        if (Data.PlayerSkillPresentationManager != null)
            await Data.PlayerSkillPresentationManager.Initialize(playerSkills);

        if (Data.OpponentSkillPresenationManager != null)
            await Data.OpponentSkillPresenationManager.Initialize(opponentSkills);
    }

    internal static IReadOnlyCollection<string> CaptureExpectedCombatCardInstanceIds(
        CombatSequenceMessages sequence
    )
    {
        var expected = Data.GetCards<ItemCard>(ECombatantId.Player, EInventorySection.Hand)
            .Select(card => card.InstanceId.ToString())
            .Where(instanceId => !string.IsNullOrWhiteSpace(instanceId))
            .ToHashSet(StringComparer.Ordinal);

        var spawnEvents = sequence.SpawnMessage?.Data?.Events;
        if (spawnEvents == null)
            return expected;

        foreach (
            var spawnEvent in spawnEvents
                .OfType<GameSimEventCardSpawned>()
                .Where(spawnEvent =>
                    spawnEvent.CombatantId == ECombatantId.Opponent
                    && spawnEvent.Section == EInventorySection.Hand
                    && spawnEvent.Type != ECardType.SocketEffect
                )
        )
        {
            if (!string.IsNullOrWhiteSpace(spawnEvent.InstanceId))
                expected.Add(spawnEvent.InstanceId);
        }

        return expected;
    }

    internal static bool ContainsAllExpectedCardIds(
        IEnumerable<string> expected,
        IEnumerable<string> observed
    )
    {
        var observedIds = observed.ToHashSet(StringComparer.Ordinal);
        return expected.All(observedIds.Contains);
    }

    internal static async Task WaitForPresentationReadyAsync(
        IReadOnlyCollection<string> expectedCardInstanceIds
    )
    {
        // ReplayState.OnEnter launches SpawnCombatCards as async void. Yield first so its
        // BoardManager.SpawnCards work has actually entered the updating state before checking
        // the negative "not busy" flags below.
        await Task.Yield();
        await BootstrapManagerInitializer.WaitUntilAsync(
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
                var activeCardIds =
                    Data.CardAndSkillLookup?.CardControllerDictionary.Where(pair =>
                            pair.Value?.gameObject != null
                            && pair.Value.gameObject.activeInHierarchy
                        )
                        .Select(pair => pair.Key.InstanceId.ToString())
                    ?? Enumerable.Empty<string>();

                return !boardManager.StorageMoving
                    && !boardManager.IsUpdatingBoard
                    && !boardManager.IsUpdatingSkillBoard
                    && playerSkillPresentationReady
                    && opponentSkillPresentationReady
                    && !boardManager.isUpdatingPresentation
                    && !boardManager.IsCarpetUnrolling
                    && !boardManager.HasCardsToReveal()
                    && ContainsAllExpectedCardIds(expectedCardInstanceIds, activeCardIds);
            },
            timeout: TimeSpan.FromSeconds(5)
        );

        // The controllers are mounted and active now; allow one render turn before removing the
        // loading scene and publishing the recording start.
        await Task.Yield();
    }
}
