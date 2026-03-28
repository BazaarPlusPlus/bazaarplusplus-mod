#nullable enable

namespace BazaarPlusPlus.Game.Lobby;

internal static class LegendaryLobbyRatingFormatter
{
    public static string BuildLegendaryText(int? leaderboardPosition, int rating)
    {
        return leaderboardPosition.HasValue && leaderboardPosition.Value > 0
            ? $"#{leaderboardPosition.Value} | {rating}"
            : rating.ToString();
    }
}
