#nullable enable

namespace BazaarPlusPlus.Game.EndOfRun;

internal static class LegendaryEndOfMatchRatingDisplayPolicy
{
    public static bool ShouldShow(bool isLegendary, int? ratingBeforeRun, int? ratingAfterRun)
    {
        return isLegendary && ratingBeforeRun.HasValue && ratingAfterRun.HasValue;
    }
}
