#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatLog;

internal static class CombatLogFrameSummaryBuilder
{
    internal static string Build(CombatLogFrame frame, CombatLogDisplayOptions options)
    {
        var parts = new List<string>();

        var eventCount = frame.Events.CountFiltered(entry =>
        {
            var category = CombatLogFormatter.GetEventCategory(entry.EventType);
            return category is CombatLogRowCategory.Event or CombatLogRowCategory.Death
                && CombatLogFormatter.ShouldInclude(options, category);
        }
        );
        AppendPart(parts, eventCount, "event");

        var combatantCount = CountCombatantUpdates(frame, options);
        AppendPart(parts, combatantCount, "combatant update");

        var cardCount = CountCardUpdates(frame, options);
        AppendPart(parts, cardCount, "card update");

        var rewardCount = frame.Events.CountFiltered(entry =>
            CombatLogFormatter.ShouldInclude(options, CombatLogRowCategory.Reward)
            && CombatLogFormatter.GetEventCategory(entry.EventType) == CombatLogRowCategory.Reward
        );
        AppendPart(parts, rewardCount, "reward");

        var systemCount = frame.Events.CountFiltered(entry =>
            CombatLogFormatter.ShouldInclude(options, CombatLogRowCategory.System)
            && CombatLogFormatter.GetEventCategory(entry.EventType) == CombatLogRowCategory.System
        );
        AppendPart(parts, systemCount, "system event");

        var unknownCount = frame.Events.CountFiltered(entry =>
            CombatLogFormatter.ShouldInclude(options, CombatLogRowCategory.Unknown)
            && CombatLogFormatter.GetEventCategory(entry.EventType) == CombatLogRowCategory.Unknown
        );
        AppendPart(parts, unknownCount, "unknown event");

        return parts.Count == 0
            ? CombatLogTextLocalizer.SummaryEmptyFrame()
            : string.Join(", ", parts);
    }

    private static int CountCombatantUpdates(CombatLogFrame frame, CombatLogDisplayOptions options)
    {
        if (!options.ShowCombatants)
            return 0;

        return CountSideUpdates(frame.Player) + CountSideUpdates(frame.Opponent);
    }

    private static int CountSideUpdates(CombatLogSideUpdate? side)
    {
        if (side == null)
            return 0;

        return side.HealthAdjustments.Count + side.Attributes.Count + side.Details.Count;
    }

    private static int CountCardUpdates(CombatLogFrame frame, CombatLogDisplayOptions options)
    {
        if (!options.ShowCards)
            return 0;

        var total = 0;
        foreach (var cardUpdate in frame.CardUpdates)
            total += cardUpdate.Attributes.Count + cardUpdate.Details.Count;

        return total;
    }

    private static void AppendPart(List<string> parts, int count, string singular)
    {
        if (count <= 0)
            return;

        parts.Add(CombatLogTextLocalizer.SummaryLabel(singular, count));
    }

    private static int CountFiltered<T>(this IReadOnlyList<T> items, System.Func<T, bool> predicate)
    {
        var count = 0;
        foreach (var item in items)
        {
            if (predicate(item))
                count++;
        }

        return count;
    }
}
