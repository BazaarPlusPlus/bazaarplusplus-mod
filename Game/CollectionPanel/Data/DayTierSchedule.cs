#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Hardcoded approximation of the game's per-day tier-probability table (tierManager.json, loaded
// by StaticDataTierRepository and consumed by BazaarCardDealer.GetProbabilitiesByDay). The real
// table is data-driven and shifts with balance patches; this is the mod-side stable approximation,
// revisit after balance patches. All thresholds live here as the single source of truth.
internal static class DayTierSchedule
{
    // Default upper bound for the day picker (matches the game's default NumDays). When the
    // current run day exceeds this, BuildDayRange extends the range out to the current day.
    public const int DefaultMaxPickerDay = 10;

    public static ETier CeilingTier(int day) =>
        day switch
        {
            <= 1 => ETier.Bronze, // Day 1
            <= 5 => ETier.Silver, // Day 2–5
            <= 7 => ETier.Gold, // Day 6–7
            _ => ETier.Diamond, // Day 8+
        };

    // A card can appear on a given day iff its StartingTier <= that day's ceiling. Legendary is
    // aliased to Diamond (matching the game's TCardItem.TryGetTierTemplate Legendary->Diamond
    // alias) so the highest day never hides Legendary-start cards.
    public static bool AllowsStartingTier(ETier startingTier, int day)
    {
        var effective = startingTier == ETier.Legendary ? ETier.Diamond : startingTier;
        return CollectionCardFacetRanks.TierRank(effective)
            <= CollectionCardFacetRanks.TierRank(CeilingTier(day));
    }
}
