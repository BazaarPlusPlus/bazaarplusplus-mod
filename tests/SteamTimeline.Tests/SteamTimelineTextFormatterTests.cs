#nullable enable
using BazaarPlusPlus.GameInterop.SteamTimeline;
using Xunit;

namespace BazaarPlusPlus.Game.SteamTimeline;

public sealed class SteamTimelineTextFormatterTests
{
    [Theory]
    [InlineData("schinese", (int)SteamTimelineLocale.SimplifiedChinese)]
    [InlineData("zh-Hans", (int)SteamTimelineLocale.SimplifiedChinese)]
    [InlineData("tchinese", (int)SteamTimelineLocale.TraditionalChinese)]
    [InlineData("zh-Hant", (int)SteamTimelineLocale.TraditionalChinese)]
    [InlineData("german", (int)SteamTimelineLocale.English)]
    [InlineData(null, (int)SteamTimelineLocale.English)]
    public void Steam_language_maps_to_the_supported_locale(string? language, int expected) =>
        Assert.Equal(
            (SteamTimelineLocale)expected,
            SteamTimelineTextFormatter.ResolveLocale(language)
        );

    [Fact]
    public void Battle_range_is_featured_but_result_marker_does_not_request_another_clip()
    {
        var battle = new SteamTimelineBattleContext(
            "battle-1",
            day: 8,
            playerHero: "Vanessa",
            opponentHero: "Mak"
        );

        var started = SteamTimelineTextFormatter.BattleStarted(battle, SteamTimelineLocale.English);
        var completedRange = SteamTimelineTextFormatter.BattleRangeCompleted(
            battle,
            SteamTimelineBattleResult.Victory,
            SteamTimelineLocale.English
        );
        var resultMarker = SteamTimelineTextFormatter.BattleCompleted(
            battle,
            SteamTimelineBattleResult.Victory,
            SteamTimelineLocale.English
        );

        Assert.Equal("Day 8 · PvP Battle", started.Title);
        Assert.Equal("Vanessa vs Mak", started.Description);
        Assert.Equal("steam_combat", started.Icon);
        Assert.Equal(SteamTimelineClipPriority.Featured, started.ClipPriority);
        Assert.Equal("steam_trophy", completedRange.Icon);
        Assert.Equal(SteamTimelineClipPriority.Featured, completedRange.ClipPriority);
        Assert.Equal("Battle ended · Victory", resultMarker.Title);
        Assert.Equal(SteamTimelineClipPriority.None, resultMarker.ClipPriority);
    }

    [Fact]
    public void Level_marker_uses_built_in_numeric_icon_and_chinese_copy()
    {
        var text = SteamTimelineTextFormatter.LevelReached(
            6,
            day: 4,
            hero: "Vanessa",
            SteamTimelineLocale.SimplifiedChinese
        );

        Assert.Equal("升至 6 级", text.Title);
        Assert.Equal("第 4 天 · Vanessa", text.Description);
        Assert.Equal("steam_6", text.Icon);
        Assert.Equal(SteamTimelineClipPriority.None, text.ClipPriority);
    }

    [Fact]
    public void Traditional_chinese_matchup_uses_traditional_copy()
    {
        var battle = new SteamTimelineBattleContext(
            "battle-1",
            day: 8,
            playerHero: "Vanessa",
            opponentHero: "Mak"
        );

        var text = SteamTimelineTextFormatter.BattleStarted(
            battle,
            SteamTimelineLocale.TraditionalChinese
        );

        Assert.Equal("第 8 天 · 玩家對戰", text.Title);
        Assert.Equal("Vanessa 對陣 Mak", text.Description);
    }

    [Fact]
    public void Dynamic_labels_drop_controls_and_are_bounded()
    {
        var dirty = "  Hero\0\n" + new string('x', 80) + "  ";

        var clean = SteamTimelineTextFormatter.CleanDynamicLabel(dirty);

        Assert.NotNull(clean);
        Assert.DoesNotContain('\0', clean!);
        Assert.DoesNotContain('\n', clean!);
        Assert.Equal(48, clean!.Length);
    }

    [Fact]
    public void Phase_id_is_stable_trimmed_and_does_not_expose_the_server_run_id()
    {
        const string raw = "sensitive-server-run-id";

        var first = SteamTimelinePhaseId.FromRunId(raw);
        var second = SteamTimelinePhaseId.FromRunId($"  {raw}  ");

        Assert.Equal(first, second);
        Assert.StartsWith("bpp-", first, StringComparison.Ordinal);
        Assert.Equal(36, first!.Length);
        Assert.DoesNotContain(raw, first, StringComparison.Ordinal);
        Assert.Null(SteamTimelinePhaseId.FromRunId("  "));
    }
}
