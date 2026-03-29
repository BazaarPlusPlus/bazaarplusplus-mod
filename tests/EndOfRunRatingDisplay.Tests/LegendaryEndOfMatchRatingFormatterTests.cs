using BazaarPlusPlus.Game.Lobby;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class LegendaryEndOfMatchRatingFormatterTests
{
    [Fact]
    public void BuildLegendaryText_FormatsLeaderboardPositionAndRating()
    {
        var text = LegendaryLobbyRatingFormatter.BuildLegendaryText(123, 1436);

        Assert.Equal("#123 | 1436", text);
    }

    [Fact]
    public void BuildLegendaryText_FallsBackToRating_WhenLeaderboardPositionMissing()
    {
        var text = LegendaryLobbyRatingFormatter.BuildLegendaryText(null, 1436);

        Assert.Equal("1436", text);
    }

}
