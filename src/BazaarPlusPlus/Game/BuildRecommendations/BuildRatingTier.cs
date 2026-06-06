#nullable enable
namespace BazaarPlusPlus.Game.BuildRecommendations;

/// <summary>
/// Maps a live player rating to the final-build rating tier bucket key.
/// Mirrors the analyzer's rating_tier_bucket boundaries (spec §6.1):
/// &lt;=500 low, &lt;=899 mid, else high. A null rating falls back to the cross-tier "all".
/// </summary>
internal static class BuildRatingTier
{
    public const string All = "all";
    public const string Low = "low";
    public const string Mid = "mid";
    public const string High = "high";

    public static string FromRating(int? rating)
    {
        if (rating is not int value)
            return All;
        if (value <= 500)
            return Low;
        if (value <= 899)
            return Mid;
        return High;
    }
}
