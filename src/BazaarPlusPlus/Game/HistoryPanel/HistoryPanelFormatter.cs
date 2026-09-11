#nullable enable
using System.Globalization;
using BazaarPlusPlus.Game.HistoryPanel.Data;
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
            null => HistoryPanelText.Unknown(),
            _ => char.ToUpperInvariant(rawStatus[0]) + rawStatus[1..],
        };
    }

    public static string FormatBattleResult(HistoryBattleRecord battle)
    {
        if (string.IsNullOrWhiteSpace(battle.Result))
            return HistoryPanelText.Unknown();

        return IsBattleWin(battle) ? HistoryPanelText.Win()
            : IsBattleLoss(battle) ? HistoryPanelText.Loss()
            : battle.Result;
    }

    public static bool IsBattleWin(HistoryBattleRecord battle)
    {
        return string.Equals(battle.Result, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(battle.Result, "Won", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBattleLoss(HistoryBattleRecord battle)
    {
        return string.Equals(battle.Result, "Loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(battle.Result, "Lost", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGhostOpponentEliminated(HistoryBattleRecord? battle)
    {
        if (battle == null || battle.Source != HistoryBattleSource.Ghost)
            return false;

        return battle.IsFinalBattle && IsBattleWinFromLocalPerspective(battle);
    }

    private static bool IsBattleWinFromLocalPerspective(HistoryBattleRecord battle)
    {
        return IsBattleWin(battle)
            || string.Equals(
                battle.WinnerCombatantId,
                "Player",
                StringComparison.OrdinalIgnoreCase
            );
    }

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
