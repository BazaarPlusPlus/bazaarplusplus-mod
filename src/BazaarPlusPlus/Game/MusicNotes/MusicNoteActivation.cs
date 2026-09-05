#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Domain.Targeting;

namespace BazaarPlusPlus.Game.MusicNotes;

internal static class MusicNoteActivation
{
    // Evaluate the actual occupying-card conditions, including cooldown and other non-tag
    // gates. Merely having an item over the socket does not mean it benefits from the note.
    internal static bool IsSatisfied(
        TCardMusicNoteSocketEffect template,
        ICard? item,
        IRun run,
        ICard note
    )
    {
        if (item == null || template.Auras == null)
            return false;
        var context = new CardConditionalContext(run, new List<ICard> { item }, note, null);
        foreach (var aura in template.Auras.Values)
        {
            if (
                aura?.Action
                    is TAuraActionCardBase
                    {
                        Target: TTargetCardOccupying { Conditions: ITCardConditional condition }
                    }
                && condition.IsSatisfiedBy(item, context)
            )
                return true;
        }
        return false;
    }
}
