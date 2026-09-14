#nullable enable
using System.Globalization;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum RunOutcomeTier
{
    Misfortune,
    Bronze,
    Silver,
    Gold,
    Diamond,
}

internal static class HistoryPanelFormatter
{
    public static string RunListText(HistoryRunRecord run) =>
        $"{HistoryPanelHeroPresentation.DisplayName(run.Hero)} · {FormatRunStatus(run.RawStatus)}\n{Mode(run)} · {HistoryPanelText.RankLabel(run.PlayerRank, run.PlayerRating)}\n{run.Victories ?? 0}–{run.Losses ?? 0} · {FormatTimestamp(run.EndedAtUtc ?? run.LastSeenAtUtc)}";

    public static string GhostListText(HistoryBattleRecord battle) =>
        $"{battle.OpponentName ?? HistoryPanelText.UnknownOpponent()} · {FormatBattleResult(battle)}\n{HistoryPanelText.DayBadge(battle.Day)} · {HistoryPanelText.RankLabel(battle.OpponentRank, battle.OpponentRating)}\n{FormatTimestamp(battle.RecordedAtUtc)}";

    private static string Mode(HistoryRunRecord run) =>
        run.GameMode.Trim().ToLowerInvariant() switch
        {
            "ranked" => LocalizedTextHelpers.Resolve(new LocalizedTextSet("Ranked", "排位")),
            "unranked" => HistoryPanelText.Unranked(),
            _ => HistoryPanelText.Unknown(),
        };

    public static string RunSummary(HistoryRunRecord? run)
    {
        if (run == null)
            return string.Empty;
        var duration = (run.EndedAtUtc ?? run.LastSeenAtUtc) - run.StartedAtUtc;
        return $"{HistoryPanelText.DayBadge(run.FinalDay)} · {HistoryPanelText.HourBadge(run.FinalHour)} · {Math.Max(0, (int)duration.TotalHours):00}:{Math.Max(0, duration.Minutes):00} · {Mode(run)} · {HistoryPanelText.RankLabel(run.PlayerRank, run.PlayerRating)}";
    }

    public static string RunFacts(HistoryRunRecord? run) =>
        run == null
            ? string.Empty
            : $"{HistoryPanelText.StatHealthShort()}  {run.MaxHealth?.ToString() ?? "—"}\n{HistoryPanelText.StatPrestigeShort()}  {run.Prestige?.ToString() ?? "—"}\n{HistoryPanelText.StatLevelShort()}  {run.Level?.ToString() ?? "—"}\n{HistoryPanelText.StatIncomeShort()}  {run.Income?.ToString() ?? "—"}\n{HistoryPanelText.StatGoldShort()}  {run.Gold?.ToString() ?? "—"}";

    public static string PageRange(HistoryCursor? first, HistoryCursor? last) =>
        first.HasValue
        && last.HasValue
        && DateTimeOffset.TryParse(first.Value.Time, out var start)
        && DateTimeOffset.TryParse(last.Value.Time, out var end)
            ? $"{FormatTimestamp(start)} → {FormatTimestamp(end)}"
            : HistoryPanelText.Unknown();

    public static string ShortenRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return HistoryPanelText.UnknownRun();

        return runId.Length <= 14 ? runId : runId[..14];
    }

    public static RunOutcomeTier? GetRunOutcomeTier(HistoryRunRecord run)
    {
        if (!string.Equals(run.RawStatus, "completed", StringComparison.OrdinalIgnoreCase))
            return null;

        var wins = run.Victories ?? 0;
        var losses = run.Losses ?? 0;
        var totalBattles = wins + losses;

        if (wins == 10 && totalBattles == 10)
            return RunOutcomeTier.Diamond;

        if (wins >= 10 && totalBattles > 10)
            return RunOutcomeTier.Gold;

        if (wins >= 7)
            return RunOutcomeTier.Silver;

        if (wins >= 4)
            return RunOutcomeTier.Bronze;

        return RunOutcomeTier.Misfortune;
    }

    public static string FormatRunStatus(string? rawStatus)
    {
        return rawStatus switch
        {
            "completed" => HistoryPanelText.Completed(),
            "abandoned" => HistoryPanelText.Abandoned(),
            "active" => HistoryPanelText.Active(),
            null or "" => HistoryPanelText.Unknown(),
            _ => char.ToUpperInvariant(rawStatus[0]) + rawStatus[1..],
        };
    }

    public static string FormatBattleResult(HistoryBattleRecord battle)
    {
        return IsBattleWin(battle) ? HistoryPanelText.Win()
            : IsBattleLoss(battle) ? HistoryPanelText.Loss()
            : HistoryPanelText.Unknown();
    }

    public static bool IsBattleWin(HistoryBattleRecord battle) =>
        HistoryPanelGhostBattleFilter.ResolveOutcome(battle) == HistoryPanelGhostBattleOutcome.Won;

    public static bool IsBattleLoss(HistoryBattleRecord battle) =>
        HistoryPanelGhostBattleFilter.ResolveOutcome(battle) == HistoryPanelGhostBattleOutcome.Lost;

    public static bool IsGhostOpponentEliminated(HistoryBattleRecord? battle) =>
        battle?.Source == HistoryBattleSource.Ghost && battle.IsFinalBattle && IsBattleWin(battle);

    public static string FormatDayOnly(int? day)
    {
        return HistoryPanelText.DayBadge(day);
    }

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("g", ResolveTimestampCulture());
    }

    private static CultureInfo ResolveTimestampCulture()
    {
        var languageCode = L.CurrentLanguageCode?.Trim().Replace('_', '-') ?? string.Empty;
        if (LanguageCodeMatcher.IsChinese(languageCode))
            languageCode = L.CurrentMode == BppChineseLocaleMode.Taiwan ? "zh-TW" : "zh-CN";

        return TryResolveSpecificCulture(languageCode, out var culture)
            ? culture
            : CultureInfo.CurrentCulture;
    }

    private static bool TryResolveSpecificCulture(string cultureName, out CultureInfo culture)
    {
        try
        {
            culture = CultureInfo.GetCultureInfo(cultureName);
            if (culture.IsNeutralCulture)
                culture = CultureInfo.CreateSpecificCulture(cultureName);
            return true;
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.CurrentCulture;
            return false;
        }
    }
}
