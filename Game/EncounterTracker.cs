#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Combat;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.EncounterTracking;
using BazaarPlusPlus.Game.MonsterPreview;
using TheBazaar;

namespace BazaarPlusPlus;

internal static class EncounterTracker
{
    private static EncounterTrackingModule? Module;
    private static bool _subscribed;

    internal static IEncounterSelectionQuery SelectionQuery => RequireModule().Query;

    internal static void Initialize(IBppEventBus eventBus)
    {
        if (eventBus == null)
            throw new ArgumentNullException(nameof(eventBus));
        if (Module != null)
            return;

        Module = new EncounterTrackingModule(eventBus);
    }

    public static void Subscribe()
    {
        Initialize(BppRuntimeHost.EventBus);
        if (_subscribed)
            return;

        RequireModule().Start();
        Events.CardDealtSimEvent.AddListener(OnCardDealt, null);
        _subscribed = true;
        BppLog.Info("EncounterTracker", "Subscribed to CardDealtSimEvent");
    }

    private static void OnCardDealt(List<Card> dealtCards)
    {
        if (!BppRuntimeHost.RunContext.IsInGameRun)
        {
            ResetEncounterState("Ignoring card dealt outside of an active run");
            return;
        }

        var state = Data.CurrentState;
        if (state == null)
        {
            ResetEncounterState("CurrentState is null");
            return;
        }

        var stateName = state.StateName;
        if (!IsSupportedSelectionState(stateName))
        {
            ResetEncounterState($"Ignoring unsupported state {stateName}");
            return;
        }

        // Use SelectionSet as the authoritative list of what's shown to the player.
        var selectionSet = state.SelectionSet;
        if (selectionSet == null || selectionSet.Count == 0)
        {
            ResetEncounterState($"SelectionSet empty in state {stateName}");
            return;
        }

        var cards = selectionSet
            .Select(id => Data.Entities.GetValueOrDefault(new InstanceId(id)))
            .Where(c => c != null)
            .ToList();

        if (cards.Count == 0)
        {
            ResetEncounterState($"SelectionSet resolved to zero cards in state {stateName}");
            return;
        }

        var cardInfos = GameDataReader.GetCardInfo(cards);
        var monsterPreviews = BuildMonsterPreviews(cards);

        var normalizedMonsterPreviews = monsterPreviews.Count > 0 ? monsterPreviews : null;
        RequireModule().UpdateSelection(stateName, cardInfos, normalizedMonsterPreviews);
        var snapshot = SelectionQuery.GetSnapshot();
        BppLog.Debug(
            "EncounterTracker",
            stateName == ERunState.Encounter
                ? $"Updated map encounters: count={cardInfos.Count}, monsterPreviews={snapshot.EncounterMonsterPreviews?.Count ?? 0}"
                : $"Updated encounter choices: state={stateName}, count={cardInfos.Count}, monsterPreviews={snapshot.EncounterMonsterPreviews?.Count ?? 0}"
        );

        BppLog.Debug("EncounterTracker", $"State={stateName}, choiceCount={cards.Count}");
    }

    private static List<RunInfo.MonsterPreview> BuildMonsterPreviews(List<Card> cards)
    {
        var previews = new List<RunInfo.MonsterPreview>();

        foreach (var card in cards)
        {
            // Only care about combat encounters
            if (!(card.Template is TCardEncounterCombat combat))
                continue;

            var encounterName = card.Template.InternalName;
            var monster = combat.CombatantType as TCombatantMonster;
            MonsterDatabase.TryGetByEncounterId(card.TemplateId, out var monsterInfo);
            var previewModel =
                monsterInfo != null
                    ? MonsterDatabasePreviewDataSource.BuildModel(
                        monsterInfo,
                        "encounter_tracker_cache"
                    )
                    : null;

            previews.Add(
                new RunInfo.MonsterPreview
                {
                    EncounterTemplateId = card.TemplateId,
                    EncounterId = monsterInfo?.EncounterId ?? Guid.Empty,
                    EncounterShortId = monsterInfo?.EncounterShortId ?? string.Empty,
                    EncounterName = encounterName,
                    Title = monsterInfo?.Title ?? string.Empty,
                    MonsterTemplateId = monster?.MonsterTemplateId.ToString(),
                    CombatLevel = monster == null ? null : (int?)monster.Level,
                    Health = monsterInfo?.Health,
                    RewardGold = combat.RewardCombatGold,
                    RewardXp = combat.RewardCombatXp,
                    SandstormEnabled = combat.SandstormEnabled,
                    BoardCards =
                        previewModel != null
                            ? EncounterPreviewSpecConverter.ToCachedCards(previewModel.ItemCards)
                            : null,
                    Skills =
                        previewModel != null
                            ? EncounterPreviewSpecConverter.ToCachedCards(previewModel.SkillCards)
                            : null,
                }
            );
        }

        return previews;
    }

    internal static bool IsSupportedSelectionState(ERunState stateName)
    {
        return stateName == ERunState.Encounter
            || stateName == ERunState.Choice
            || stateName == ERunState.Loot
            || stateName == ERunState.Pedestal;
    }

    internal static void ResetEncounterState(string reason)
    {
        var snapshot = SelectionQuery.GetSnapshot();
        var hadState =
            snapshot.AvailableEncounters != null
            || snapshot.CurrentEncounterChoices != null
            || snapshot.EncounterMonsterPreviews != null;

        RequireModule().Clear();

        if (hadState)
            BppLog.Debug("EncounterTracker", $"Cleared encounter state: {reason}");
        else
            BppLog.Debug("EncounterTracker", $"Encounter state already empty: {reason}");
    }

    private static EncounterTrackingModule RequireModule()
    {
        return Module
            ?? throw new InvalidOperationException("EncounterTracker is not initialized.");
    }
}
