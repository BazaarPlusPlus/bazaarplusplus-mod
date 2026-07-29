#nullable enable
using System.Globalization;
using BazaarPlusPlus.GameInterop.SteamTimeline;

namespace BazaarPlusPlus.Game.SteamTimeline;

internal static class SteamTimelineTextFormatter
{
    private const uint PhaseMetadataPriority = 10;
    private const uint LevelPriority = 50;
    private const uint BattlePriority = 100;
    private const uint ResultPriority = 110;
    private const int MaximumDynamicLabelLength = 48;

    internal static SteamTimelineLocale ResolveLocale(string? steamUiLanguage) =>
        steamUiLanguage?.Trim().ToLowerInvariant() switch
        {
            "schinese" or "simplified chinese" or "zh-cn" or "zh-hans" =>
                SteamTimelineLocale.SimplifiedChinese,
            "tchinese" or "traditional chinese" or "zh-tw" or "zh-hant" =>
                SteamTimelineLocale.TraditionalChinese,
            _ => SteamTimelineLocale.English,
        };

    internal static SteamTimelineEventText BattleStarted(
        SteamTimelineBattleContext battle,
        SteamTimelineLocale locale
    )
    {
        var day = DayLabel(battle.Day);
        var title = locale switch
        {
            SteamTimelineLocale.SimplifiedChinese => day == null ? "玩家对战" : $"{day} · 玩家对战",
            SteamTimelineLocale.TraditionalChinese => day == null
                ? "玩家對戰"
                : $"{day} · 玩家對戰",
            _ => day == null ? "PvP Battle" : $"{day} · PvP Battle",
        };
        var playerHero = CleanDynamicLabel(battle.PlayerHero);
        var opponentHero = CleanDynamicLabel(battle.OpponentHero);
        var description = BuildMatchup(playerHero, opponentHero, locale);
        return new SteamTimelineEventText(
            title,
            description,
            "steam_attack",
            BattlePriority,
            SteamTimelineClipPriority.Featured
        );
    }

    internal static SteamTimelineEventText BattleCompleted(
        SteamTimelineBattleContext battle,
        SteamTimelineBattleResult result,
        SteamTimelineLocale locale
    )
    {
        var victory = result == SteamTimelineBattleResult.Victory;
        var title = locale switch
        {
            SteamTimelineLocale.SimplifiedChinese => victory
                ? "战斗结束 · 胜利"
                : "战斗结束 · 失败",
            SteamTimelineLocale.TraditionalChinese => victory
                ? "戰鬥結束 · 勝利"
                : "戰鬥結束 · 失敗",
            _ => victory ? "Battle ended · Victory" : "Battle ended · Defeat",
        };
        var description = BattleStarted(battle, locale).Description;
        return new SteamTimelineEventText(
            title,
            description,
            victory ? "steam_trophy" : "steam_death",
            ResultPriority,
            SteamTimelineClipPriority.None
        );
    }

    internal static SteamTimelineEventText BattleRangeCompleted(
        SteamTimelineBattleContext battle,
        SteamTimelineBattleResult result,
        SteamTimelineLocale locale
    )
    {
        var started = BattleStarted(battle, locale);
        var resultLabel = (result, locale) switch
        {
            (SteamTimelineBattleResult.Victory, SteamTimelineLocale.SimplifiedChinese) => "胜利",
            (SteamTimelineBattleResult.Victory, SteamTimelineLocale.TraditionalChinese) => "勝利",
            (SteamTimelineBattleResult.Victory, _) => "Victory",
            (SteamTimelineBattleResult.Defeat, SteamTimelineLocale.SimplifiedChinese) => "失败",
            (SteamTimelineBattleResult.Defeat, SteamTimelineLocale.TraditionalChinese) => "失敗",
            _ => "Defeat",
        };
        return new SteamTimelineEventText(
            $"{started.Title} · {resultLabel}",
            started.Description,
            result == SteamTimelineBattleResult.Victory ? "steam_trophy" : "steam_death",
            ResultPriority,
            SteamTimelineClipPriority.Featured
        );
    }

    internal static SteamTimelineEventText BattleInterrupted(
        SteamTimelineBattleContext battle,
        SteamTimelineLocale locale
    )
    {
        var title = locale switch
        {
            SteamTimelineLocale.SimplifiedChinese => "战斗中断",
            SteamTimelineLocale.TraditionalChinese => "戰鬥中斷",
            _ => "Battle interrupted",
        };
        return new SteamTimelineEventText(
            title,
            BattleStarted(battle, locale).Description,
            "steam_caution",
            BattlePriority,
            SteamTimelineClipPriority.None
        );
    }

    internal static SteamTimelineEventText LevelReached(
        int level,
        int? day,
        string? hero,
        SteamTimelineLocale locale
    )
    {
        var cleanHero = CleanDynamicLabel(hero);
        var description = BuildProgressDescription(level, day, cleanHero, locale);
        return new SteamTimelineEventText(
            "UP",
            description,
            "steam_starburst",
            LevelPriority,
            SteamTimelineClipPriority.None
        );
    }

    internal static string HeroGroup(SteamTimelineLocale locale) =>
        locale switch
        {
            SteamTimelineLocale.SimplifiedChinese or SteamTimelineLocale.TraditionalChinese =>
                "英雄",
            _ => "Hero",
        };

    internal static string RunResultGroup(SteamTimelineLocale locale) =>
        locale switch
        {
            SteamTimelineLocale.SimplifiedChinese => "对局结果",
            SteamTimelineLocale.TraditionalChinese => "對局結果",
            _ => "Run Result",
        };

    internal static string RunResultLabel(SteamTimelineRunExit exit, SteamTimelineLocale locale) =>
        (exit, locale) switch
        {
            (SteamTimelineRunExit.Completed, SteamTimelineLocale.SimplifiedChinese) => "对局完成",
            (SteamTimelineRunExit.Completed, SteamTimelineLocale.TraditionalChinese) => "對局完成",
            (SteamTimelineRunExit.Completed, _) => "Run completed",
            (SteamTimelineRunExit.Interrupted, SteamTimelineLocale.SimplifiedChinese) => "对局中断",
            (SteamTimelineRunExit.Interrupted, SteamTimelineLocale.TraditionalChinese) =>
                "對局中斷",
            (SteamTimelineRunExit.Interrupted, _) => "Run interrupted",
            _ => string.Empty,
        };

    internal static string FinalDayGroup(SteamTimelineLocale locale) =>
        locale switch
        {
            SteamTimelineLocale.SimplifiedChinese => "最终天数",
            SteamTimelineLocale.TraditionalChinese => "最終天數",
            _ => "Final Day",
        };

    internal static string WinsGroup(SteamTimelineLocale locale) =>
        locale switch
        {
            SteamTimelineLocale.SimplifiedChinese => "胜场",
            SteamTimelineLocale.TraditionalChinese => "勝場",
            _ => "Wins",
        };

    internal static string? CleanDynamicLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var clean = new string(
            value.Where(character => !char.IsControl(character)).ToArray()
        ).Trim();
        if (clean.Length == 0)
            return null;
        return clean.Length <= MaximumDynamicLabelLength
            ? clean
            : clean.Substring(0, MaximumDynamicLabelLength);
    }

    internal static uint MetadataPriority => PhaseMetadataPriority;

    internal static string? DayLabel(int? day) =>
        day is > 0 ? $"D{day.Value.ToString(CultureInfo.InvariantCulture)}" : null;

    private static string BuildMatchup(
        string? playerHero,
        string? opponentHero,
        SteamTimelineLocale locale
    )
    {
        if (playerHero != null && opponentHero != null)
        {
            return locale switch
            {
                SteamTimelineLocale.SimplifiedChinese => $"{playerHero} 对阵 {opponentHero}",
                SteamTimelineLocale.TraditionalChinese => $"{playerHero} 對陣 {opponentHero}",
                _ => $"{playerHero} vs {opponentHero}",
            };
        }

        return playerHero ?? opponentHero ?? string.Empty;
    }

    private static string BuildProgressDescription(
        int level,
        int? day,
        string? hero,
        SteamTimelineLocale locale
    )
    {
        var dayText = DayLabel(day);
        var levelText = locale switch
        {
            SteamTimelineLocale.SimplifiedChinese => $"{level} 级",
            SteamTimelineLocale.TraditionalChinese => $"{level} 級",
            _ => $"Level {level}",
        };
        if (dayText != null && hero != null)
            return $"{dayText} · {levelText} · {hero}";
        if (dayText != null)
            return $"{dayText} · {levelText}";
        return hero == null ? levelText : $"{levelText} · {hero}";
    }
}
