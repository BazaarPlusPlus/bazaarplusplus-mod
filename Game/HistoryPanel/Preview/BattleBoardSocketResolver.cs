#nullable enable
using System;

namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

// Pure socket-placement math for the battle-board preview: given the socket count, an optional
// requested socket index, a fallback index, and the card's slot span, pick the start socket the
// card should occupy — or -1 when a card of that span cannot fit. No Unity or game dependencies,
// so it is unit-testable in isolation.
internal static class BattleBoardSocketResolver
{
    public static int ResolveIndex(
        int socketCount,
        int? requestedIndex,
        int fallbackIndex,
        int span
    )
    {
        if (socketCount <= 0)
            return -1;

        var lastValidStart = socketCount - span;
        if (lastValidStart < 0)
            return -1;

        var index = requestedIndex ?? fallbackIndex;
        return Math.Clamp(index, 0, lastValidStart);
    }
}
