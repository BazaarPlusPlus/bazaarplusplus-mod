#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet RefreshFinalBuildsText = new(
        "Pull Builds",
        "拉取阵容",
        "拉取陣容",
        "拉取陣容"
    );
    private static readonly LocalizedTextSet WorkingText = new("Working...", "处理中...");
    private static readonly LocalizedTextSet RefreshingFinalBuildsText = new(
        "Pulling ten-win builds...",
        "正在拉取十胜阵容...",
        "正在拉取十勝陣容...",
        "正在拉取十勝陣容..."
    );
    private static readonly LocalizedTextSet FinalBuildRefreshAlreadyRunningText = new(
        "Build pull is already in progress.",
        "阵容拉取进行中。",
        "陣容拉取進行中。",
        "陣容拉取進行中。"
    );
    private static readonly LocalizedTextSet FinalBuildRefreshSucceededText = new(
        "Ten-win builds updated.",
        "十胜阵容已更新。",
        "十勝陣容已更新。",
        "十勝陣容已更新。"
    );
    private static readonly LocalizedTextSet CorpusDataTimeLabelText = new(
        "data",
        "数据时间",
        "資料時間",
        "資料時間"
    );
    private static readonly LocalizedTextSet CorpusBuildCountUnitText = new(
        "builds",
        "套阵容",
        "套陣容",
        "套陣容"
    );
    private static readonly LocalizedTextSet CorpusHeroCountUnitText = new(
        "heroes",
        "位英雄",
        "位英雄",
        "位英雄"
    );

    public static string RefreshFinalBuilds() => L.Resolve(RefreshFinalBuildsText);

    public static string Working() => L.Resolve(WorkingText);

    public static string RefreshingFinalBuilds() => L.Resolve(RefreshingFinalBuildsText);

    public static string FinalBuildRefreshAlreadyRunning() =>
        L.Resolve(FinalBuildRefreshAlreadyRunningText);

    public static string FinalBuildRefreshSucceeded() => L.Resolve(FinalBuildRefreshSucceededText);

    public static string FinalBuildRefreshDetail(TenWinCorpusSummary summary)
    {
        var parts = new List<string>();
        if (summary.GeneratedAtUtc.HasValue)
        {
            var localTime = summary
                .GeneratedAtUtc.Value.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            parts.Add($"{L.Resolve(CorpusDataTimeLabelText)} {localTime}");
        }

        parts.Add($"{summary.BuildCount} {L.Resolve(CorpusBuildCountUnitText)}");
        parts.Add($"{summary.HeroCount} {L.Resolve(CorpusHeroCountUnitText)}");
        parts.AddRange(
            summary
                .HeroBuildCounts.Where(count => !string.IsNullOrWhiteSpace(count.Hero))
                .Select(count => $"{count.Hero} {count.BuildCount}")
        );
        return string.Join(" · ", parts);
    }

    public static string FinalBuildRefreshFailed(string details) =>
        L.Resolve(
            new LocalizedTextSet(
                $"Couldn't pull ten-win builds: {details}",
                $"拉取十胜阵容失败：{details}",
                $"拉取十勝陣容失敗：{details}",
                $"拉取十勝陣容失敗：{details}"
            )
        );
}
