#nullable enable
using System;

namespace BazaarPlusPlus.Game.RunLogging.Models;

internal static class RunLogEventKinds
{
    public static bool IsSelectionSeenKind(string? kind)
    {
        return string.Equals(kind, "selection_seen", StringComparison.Ordinal)
            || string.Equals(kind, "encounter_options_seen", StringComparison.Ordinal)
            || string.Equals(kind, "choice_options_seen", StringComparison.Ordinal)
            || string.Equals(kind, "loot_options_seen", StringComparison.Ordinal)
            || string.Equals(kind, "pedestal_options_seen", StringComparison.Ordinal);
    }

    public static bool IsChoiceMadeKind(string? kind)
    {
        return string.Equals(kind, "choice_made", StringComparison.Ordinal)
            || string.Equals(kind, "encounter_selected", StringComparison.Ordinal)
            || string.Equals(kind, "choice_selected", StringComparison.Ordinal)
            || string.Equals(kind, "loot_selected", StringComparison.Ordinal)
            || string.Equals(kind, "pedestal_selected", StringComparison.Ordinal);
    }

    public static bool ClearsPendingSelectionKind(string? kind)
    {
        return IsChoiceMadeKind(kind)
            || string.Equals(kind, "selection_abandoned", StringComparison.Ordinal);
    }
}
