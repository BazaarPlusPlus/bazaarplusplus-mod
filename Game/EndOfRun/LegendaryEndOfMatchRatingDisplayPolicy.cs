#nullable enable

namespace BazaarPlusPlus.Game.EndOfRun;

internal static class LegendaryEndOfMatchRatingDisplayPolicy
{
    public static bool ShouldShow(bool isLegendary, int? ratingBeforeRun, int? ratingAfterRun)
    {
        return isLegendary
            && IsValidLegendaryRating(ratingBeforeRun)
            && IsValidLegendaryRating(ratingAfterRun);
    }

    private static bool IsValidLegendaryRating(int? rating)
    {
        return rating.HasValue && rating.Value > 0;
    }
}
