#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Combat;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.MonsterPreview;
using TheBazaar;

namespace BazaarPlusPlus.Game.EncounterTracking;

internal sealed class EncounterTrackingController
{
    private readonly EncounterTrackingModule _module;
    private readonly IRunContext _runContext;
    private readonly IMonsterCatalog _monsterCatalog;
    private bool _subscribed;

    public EncounterTrackingController(
        EncounterTrackingModule module,
        IRunContext runContext,
        IMonsterCatalog monsterCatalog
    )
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        _monsterCatalog = monsterCatalog ?? throw new ArgumentNullException(nameof(monsterCatalog));
    }

    public void Start()
    {
        if (_subscribed)
            return;

        Events.CardDealtSimEvent.AddListener(OnCardDealt, null);
        _subscribed = true;
        BppLog.Info("EncounterTracker", "Subscribed to CardDealtSimEvent");
    }

    public void Stop()
    {
        if (!_subscribed)
            return;

        Events.CardDealtSimEvent.RemoveListener(OnCardDealt);
        _subscribed = false;
    }

    public void ResetEncounterState(string reason)
    {
        var snapshot = _module.Query.GetSnapshot();
        var hadState =
            snapshot.AvailableEncounters != null
            || snapshot.CurrentEncounterChoices != null
            || snapshot.EncounterMonsterPreviews != null;

        _module.Clear();

        if (hadState)
            BppLog.Debug("EncounterTracker", $"Cleared encounter state: {reason}");
        else
            BppLog.Debug("EncounterTracker", $"Encounter state already empty: {reason}");
    }

    public static bool IsSupportedSelectionState(ERunState stateName)
    {
        return stateName == ERunState.Encounter
            || stateName == ERunState.Choice
            || stateName == ERunState.Loot
            || stateName == ERunState.Pedestal;
    }

    private void OnCardDealt(List<Card> dealtCards)
    {
        if (!_runContext.IsInGameRun)
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

        _module.UpdateSelection(stateName, cardInfos, normalizedMonsterPreviews);
        var snapshot = _module.Query.GetSnapshot();
        BppLog.Debug(
            "EncounterTracker",
            stateName == ERunState.Encounter
                ? $"Updated map encounters: count={cardInfos.Count}, monsterPreviews={snapshot.EncounterMonsterPreviews?.Count ?? 0}"
                : $"Updated encounter choices: state={stateName}, count={cardInfos.Count}, monsterPreviews={snapshot.EncounterMonsterPreviews?.Count ?? 0}"
        );

        BppLog.Debug("EncounterTracker", $"State={stateName}, choiceCount={cards.Count}");
    }

    private List<RunInfo.MonsterPreview> BuildMonsterPreviews(List<Card> cards)
    {
        var previews = new List<RunInfo.MonsterPreview>();
        foreach (var card in cards)
        {
            if (!(card.Template is TCardEncounterCombat combat))
                continue;

            var encounterName = card.Template.InternalName;
            var monster = combat.CombatantType as TCombatantMonster;
            _monsterCatalog.TryGetByEncounterId(card.TemplateId, out var monsterInfo);
            var previewModel =
                monsterInfo != null
                    ? MonsterPreviewProjector.BuildModel(monsterInfo, "encounter_tracker_cache")
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
}
