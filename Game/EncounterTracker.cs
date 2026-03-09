#pragma warning disable CS0436
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
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
            return;

        // Skip combat states — card dealt events also fire during combat for spawned cards
        var stateName = state.StateName;
        if (stateName == ERunState.Combat || stateName == ERunState.PVPCombat)
            return;

        // Use SelectionSet as the authoritative list of what's shown to the player.
        // It's already updated by UpdateFromGameSimAsync before ProcessEvents runs,
        // and Data.Entities has all the cards by the time this event fires.
        var selectionSet = state.SelectionSet;
        if (selectionSet == null || selectionSet.Count == 0)
            return;

        var cards = selectionSet
            .Select(id => Data.Entities.GetValueOrDefault(new InstanceId(id)))
            .Where(c => c != null)
            .ToList();

        if (cards.Count == 0)
            return;

        var cardInfos = GameDataReader.GetCardInfo(cards);
        if (stateName == ERunState.Encounter)
            ModState.AvailableEncounters = cardInfos;
        else
            ModState.CurrentEncounterChoices = cardInfos;

        ModState.Logger.LogInfo(
            $"[EncounterTracker] State={stateName}, choices=["
            + string.Join(", ", cards.Select(c => c.Template?.InternalName ?? c.TemplateId.ToString()))
            + "]"
        );
    }
}
