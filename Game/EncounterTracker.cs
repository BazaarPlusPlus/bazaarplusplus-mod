#pragma warning disable CS0436
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Combat;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using TheBazaar;

namespace BazaarPlusPlus;

internal static class EncounterTracker
{
    public static void Subscribe()
    {
        Events.CardDealtSimEvent.AddListener(OnCardDealt, null);
        ModState.Logger.LogInfo("[EncounterTracker] Subscribed to CardDealtSimEvent");
    }

    private static void OnCardDealt(List<Card> dealtCards)
    {
        var state = Data.CurrentState;
        if (state == null)
        {
            ClearEncounterState("CurrentState is null");
            return;
        }

        // Skip combat states — card dealt events also fire during combat for spawned cards
        var stateName = state.StateName;
        if (stateName == ERunState.Combat || stateName == ERunState.PVPCombat)
        {
            ClearEncounterState($"Ignoring combat state {stateName}");
            return;
        }

        // Use SelectionSet as the authoritative list of what's shown to the player.
        var selectionSet = state.SelectionSet;
        if (selectionSet == null || selectionSet.Count == 0)
        {
            ClearEncounterState($"SelectionSet empty in state {stateName}");
            return;
        }

        var cards = selectionSet
            .Select(id => Data.Entities.GetValueOrDefault(new InstanceId(id)))
            .Where(c => c != null)
            .ToList();

        if (cards.Count == 0)
        {
            ClearEncounterState($"SelectionSet resolved to zero cards in state {stateName}");
            return;
        }

        var cardInfos = GameDataReader.GetCardInfo(cards);

        if (stateName == ERunState.Encounter)
        {
            ModState.AvailableEncounters = cardInfos;
            ModState.EncounterMonsterPreviews = BuildMonsterPreviews(cards);
            ModState.CurrentEncounterChoices = null;
            ModState.Logger.LogInfo(
                $"[EncounterTracker] Updated map encounters: count={cardInfos.Count}, monsterPreviews={ModState.EncounterMonsterPreviews?.Count ?? 0}"
            );
        }
        else
        {
            ModState.CurrentEncounterChoices = cardInfos;
            ModState.AvailableEncounters = null;
            ModState.EncounterMonsterPreviews = null;
            ModState.Logger.LogInfo(
                $"[EncounterTracker] Updated encounter choices: state={stateName}, count={cardInfos.Count}"
            );
        }

        ModState.Logger.LogInfo(
            $"[EncounterTracker] State={stateName}, choices=["
                + string.Join(
                    ", ",
                    cards.Select(c => c.Template?.InternalName ?? c.TemplateId.ToString())
                )
                + "]"
        );
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
            var entry = MonsterDatabase.TryGet(encounterName);

            previews.Add(
                new RunInfo.MonsterPreview
                {
                    EncounterName = encounterName,
                    Items = entry?.Items,
                    Skills = entry?.Skills,
                }
            );
        }

        return previews;
    }

    private static void ClearEncounterState(string reason)
    {
        var hadState =
            ModState.AvailableEncounters != null
            || ModState.CurrentEncounterChoices != null
            || ModState.EncounterMonsterPreviews != null;

        ModState.AvailableEncounters = null;
        ModState.CurrentEncounterChoices = null;
        ModState.EncounterMonsterPreviews = null;

        if (hadState)
            ModState.Logger?.LogInfo($"[EncounterTracker] Cleared encounter state: {reason}");
        else
            ModState.Logger?.LogDebug(
                $"[EncounterTracker] Encounter state already empty: {reason}"
            );
    }
}
