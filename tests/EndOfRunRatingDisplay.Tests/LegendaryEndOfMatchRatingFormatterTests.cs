using System;
using System.IO;
using BazaarPlusPlus.Game.EndOfRun;
using BazaarPlusPlus.Game.Lobby;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class LegendaryEndOfMatchRatingFormatterTests
{
    [Theory]
    [InlineData(1420, 1436, "1420 -> 1436 <color=#6DD16B>(+16)</color>")]
    [InlineData(1436, 1420, "1436 -> 1420 <color=#E06767>(-16)</color>")]
    [InlineData(1436, 1436, "1436 -> 1436 <color=#D8D8D8>(0)</color>")]
    public void BuildLine_FormatsBeforeAfterAndColoredDelta(int before, int after, string expected)
    {
        var line = LegendaryEndOfMatchRatingFormatter.BuildLine(before, after);

        Assert.Equal(expected, line);
    }

    [Fact]
    public void ShouldShow_ReturnsFalse_ForNonLegendary()
    {
        Assert.False(LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow(false, 1420, 1436));
    }

    [Fact]
    public void ShouldShow_ReturnsFalse_WhenEitherRatingIsMissing()
    {
        Assert.False(LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow(true, null, 1436));
        Assert.False(LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow(true, 1420, null));
    }

    [Fact]
    public void ShouldShow_ReturnsFalse_WhenRatingsLookUninitialized()
    {
        Assert.False(LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow(true, 0, 1436));
        Assert.False(LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow(true, 1420, 0));
        Assert.False(LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow(true, 0, 0));
    }

    [Fact]
    public void ShouldShow_ReturnsTrue_ForLegendaryWithBothRatings()
    {
        Assert.True(LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow(true, 1420, 1436));
    }

    [Fact]
    public void PatchSource_TargetsEndOfRunRankController_AndUsesLegendaryGate()
    {
        var sourcePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "Patches",
                "EndOfRun",
                "LegendaryEndOfMatchRatingPatches.cs"
            )
        );

        var source = File.ReadAllText(sourcePath);

        Assert.Contains("EndOfRunRankController", source, StringComparison.Ordinal);
        Assert.Contains("CommonData", source, StringComparison.Ordinal);
        Assert.Contains("CurrentSeasonRank", source, StringComparison.Ordinal);
        Assert.Contains("Data.Rank?.CurrentSeasonRank?.Rating", source, StringComparison.Ordinal);
        Assert.Contains("ShouldShow", source, StringComparison.Ordinal);
        Assert.Contains("sourceLabel.rectTransform", source, StringComparison.Ordinal);
        Assert.Contains("new GameObject(RatingLineObjectName", source, StringComparison.Ordinal);
    }

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

    [Fact]
    public void LobbyPatchSource_TargetsMainMenuController_AndReadsLiveRankData()
    {
        var sourcePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "Patches",
                "Lobby",
                "LegendaryLobbyRatingPatches.cs"
            )
        );

        var source = File.ReadAllText(sourcePath);

        Assert.Contains("MainMenuController", source, StringComparison.Ordinal);
        Assert.Contains("OnProfileLoaded", source, StringComparison.Ordinal);
        Assert.Contains("OnCurrentSeasonRankDataUpdated", source, StringComparison.Ordinal);
        Assert.Contains("CurrentSeasonRank", source, StringComparison.Ordinal);
        Assert.Contains("LeaderboardPosition", source, StringComparison.Ordinal);
    }
}
