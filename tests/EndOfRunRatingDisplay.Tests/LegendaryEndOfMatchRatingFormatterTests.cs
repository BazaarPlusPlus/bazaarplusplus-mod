using System;
using System.IO;
using BazaarPlusPlus.Game.Lobby;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class LegendaryEndOfMatchRatingFormatterTests
{
    [Fact]
    public void PatchSource_IsRemoved_SoEndOfRunDoesNotInjectExtraUiText()
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

        Assert.False(File.Exists(sourcePath));
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
        Assert.Contains("OnRankUpdated", source, StringComparison.Ordinal);
        Assert.Contains("CurrentSeasonRank", source, StringComparison.Ordinal);
        Assert.Contains("ClientCache.Leaderboard.Value.position", source, StringComparison.Ordinal);
    }
}
