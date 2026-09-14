#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet SelectRunSubtitleText = new(
        "Select a run to inspect its recorded battles.",
        "选择一个 run 查看其记录战斗。",
        "選擇一個 run 檢視其記錄戰鬥。"
    );

    private static readonly LocalizedTextSet AllFilterText = new("All", "全部");

    private static readonly LocalizedTextSet IWonFilterText = new("I Won", "我赢了");

    private static readonly LocalizedTextSet ILostFilterText = new("I Lost", "我输了");

    private static readonly LocalizedTextSet FilterDayMin10Text = new("≥10d", "≥10天", "≥10天");

    private static readonly LocalizedTextSet UnrankedText = new("Normal", "普通对局", "普通對局");

    private static readonly LocalizedTextSet UnknownRunText = new("Unknown Run", "未知 Run");

    private static readonly LocalizedTextSet CompletedText = new("Completed", "已完成", "已完成");

    private static readonly LocalizedTextSet AbandonedText = new("Abandoned", "已放弃", "已放棄");

    private static readonly LocalizedTextSet ActiveText = new("Active", "进行中", "進行中");

    private static readonly LocalizedTextSet UnknownText = new("Unknown", "未知");

    internal static string SelectRunSubtitle() => Resolve(SelectRunSubtitleText);

    internal static string FilterAll() => Resolve(AllFilterText);

    internal static string FilterIWon() => Resolve(IWonFilterText);

    internal static string FilterILost() => Resolve(ILostFilterText);

    internal static string FilterDayMin10() => Resolve(FilterDayMin10Text);

    internal static string Unranked() => Resolve(UnrankedText);

    internal static string UnknownRun() => Resolve(UnknownRunText);

    internal static string Completed() => Resolve(CompletedText);

    internal static string Abandoned() => Resolve(AbandonedText);

    internal static string Active() => Resolve(ActiveText);

    internal static string Unknown() => Resolve(UnknownText);

    internal static string StatHealthShort() => FormatSimple("HP", "生命");

    internal static string StatPrestigeShort() => FormatSimple("PRE", "声望", "聲望");

    internal static string StatLevelShort() => FormatSimple("LVL", "等级", "等級");

    internal static string StatIncomeShort() => FormatSimple("INC", "收入", "收入");

    internal static string StatGoldShort() => FormatSimple("GLD", "金币", "金幣");

    internal static string HourBadge(int? hour) =>
        FormatSimple(
            hour.HasValue ? $"H{hour.Value}" : "H?",
            hour.HasValue ? $"{hour.Value}时" : "?时",
            hour.HasValue ? $"{hour.Value}時" : "?時"
        );

    internal static string DayBadge(int? day) =>
        FormatSimple(
            day.HasValue ? $"D{day.Value}" : "D?",
            day.HasValue ? $"{day.Value}天" : "?天",
            day.HasValue ? $"{day.Value}天" : "?天"
        );

    internal static string RunOutcomeBubbleLabel(RunOutcomeTier tier)
    {
        return tier switch
        {
            RunOutcomeTier.Diamond => FormatSimple("DIA", "钻石"),
            RunOutcomeTier.Gold => FormatSimple("GLD", "黄金", "黃金"),
            RunOutcomeTier.Silver => FormatSimple("SLV", "白银", "白銀"),
            RunOutcomeTier.Bronze => FormatSimple("BRZ", "青铜", "青銅"),
            _ => FormatSimple("MIS", "惨淡", "慘淡"),
        };
    }

    internal static string BoardSummary(int items, int skills)
    {
        var languageCode = L.CurrentLanguageCode;
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return ResolveChinese($"{items} 物品 · {skills} 技能", $"{items} 物品 · {skills} 技能");

        return $"{items} {Pluralize(items, "item", "items")} · {skills} {Pluralize(skills, "skill", "skills")}";
    }

    internal static string RankLabel(string? rank, int? rating = null)
    {
        var normalized = rank?.Trim();
        var label = string.IsNullOrEmpty(normalized) ? Unknown() : normalized!.ToUpperInvariant();
        if (LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode) && normalized != null)
            label = normalized.ToLowerInvariant() switch
            {
                "bronze" => FormatSimple("BRZ", "青铜", "青銅"),
                "silver" => FormatSimple("SLV", "白银", "白銀"),
                "gold" => FormatSimple("GLD", "黄金", "黃金"),
                "diamond" => FormatSimple("DIA", "钻石", "鑽石"),
                "legendary" => FormatSimple("LEG", "传说", "傳說"),
                _ => label,
            };
        return rating.HasValue ? $"{label} · {rating.Value}" : label;
    }

    internal static string SelectRunToDelete()
    {
        return FormatSimple("Select a run to delete.", "请选择要删除的对局。");
    }

    internal static string ActiveRunDeleteUnavailable()
    {
        return FormatSimple("Active runs cannot be deleted.", "进行中的 run 不能删除。");
    }

    internal static string CurrentGameplayRunDeleteUnavailable()
    {
        return FormatSimple(
            "The currently active gameplay run cannot be deleted.",
            "当前正在进行的对局 run 不能删除。"
        );
    }

    internal static string RunLogRepositoryUnavailable()
    {
        return FormatSimple("Run log repository is unavailable.", "Run log 仓库不可用。");
    }

    internal static string DeleteRunConfirm(string shortRunId)
    {
        return FormatSimple(
            $"Press Delete again within 5s to remove {shortRunId}. Completed MP4 recordings will be kept.",
            $"请在 5 秒内再次点击删除，以移除 {shortRunId}。已完成的 MP4 录像会保留。"
        );
    }

    internal static string RunDeleteFailed(string details)
    {
        return FormatSimple($"Couldn't delete run: {details}", $"删除对局失败：{details}");
    }

    internal static string DeletedRun(string shortRunId, int battleCount)
    {
        if (battleCount > 0)
        {
            return FormatSimple(
                $"Removed run {shortRunId} and cleaned {battleCount} battle records. Completed MP4 recordings were kept.",
                $"已删除对局 {shortRunId}，并清理 {battleCount} 条战斗记录。已完成的 MP4 录像已保留。"
            );
        }

        return FormatSimple(
            $"Removed run {shortRunId}. Completed MP4 recordings were kept.",
            $"已删除对局 {shortRunId}。已完成的 MP4 录像已保留。"
        );
    }
}
