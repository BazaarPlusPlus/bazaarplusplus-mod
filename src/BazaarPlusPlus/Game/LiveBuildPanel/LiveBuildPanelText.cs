#nullable enable

using System.Collections.Generic;
using System.Globalization;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static class LiveBuildPanelText
{
    private static readonly LocalizedTextSet TitleText = new(
        "Final Build",
        "终局阵容",
        "終局陣容",
        "終局陣容"
    );
    private static readonly LocalizedTextSet SubtitleText = new(
        "Choose live item candidates and browse matching ten-win builds.",
        "选择当前局内物品候选，并查看匹配的十胜阵容。",
        "選擇當前局內物品候選，並查看匹配的十勝陣容。",
        "選擇當前局內物品候選，並查看匹配的十勝陣容。"
    );
    private static readonly LocalizedTextSet FinalBuildRowText = new(
        "Ten-Win Build",
        "十胜推荐",
        "十勝推薦",
        "十勝推薦"
    );
    private static readonly LocalizedTextSet ShopRowText = new(
        "Shop Items",
        "商店物品",
        "商店物品",
        "商店物品"
    );
    private static readonly LocalizedTextSet BoardRowText = new(
        "Board Items",
        "场上物品",
        "場上物品",
        "場上物品"
    );
    private static readonly LocalizedTextSet StashRowText = new(
        "Stash Items",
        "箱子物品",
        "箱子物品",
        "箱子物品"
    );
    private static readonly LocalizedTextSet CloseText = new("Close", "关闭", "關閉", "關閉");
    private static readonly LocalizedTextSet PrevText = new(
        "Previous",
        "上一条",
        "上一條",
        "上一條"
    );
    private static readonly LocalizedTextSet NextText = new("Next", "下一条", "下一條", "下一條");
    private static readonly LocalizedTextSet NoRunText = new(
        "No active run.",
        "当前没有进行中的对局。",
        "當前沒有進行中的對局。",
        "當前沒有進行中的對局。"
    );
    private static readonly LocalizedTextSet NoCandidatesText = new(
        "Select item candidates from shop, board, or stash.",
        "从商店、场上或箱子选择物品候选。",
        "從商店、場上或箱子選擇物品候選。",
        "從商店、場上或箱子選擇物品候選。"
    );
    private static readonly LocalizedTextSet NoRecommendationText = new(
        "No matching ten-win build.",
        "暂无匹配的十胜阵容。",
        "暫無匹配的十勝陣容。",
        "暫無匹配的十勝陣容。"
    );
    private static readonly LocalizedTextSet EmptyShopText = new(
        "No item options in the current shop selection.",
        "当前商店选择里没有物品。",
        "當前商店選擇裡沒有物品。",
        "當前商店選擇裡沒有物品。"
    );
    private static readonly LocalizedTextSet EmptyBoardText = new(
        "No board items.",
        "场上没有物品。",
        "場上沒有物品。",
        "場上沒有物品。"
    );
    private static readonly LocalizedTextSet EmptyStashText = new(
        "No stash items.",
        "箱子没有物品。",
        "箱子沒有物品。",
        "箱子沒有物品。"
    );
    private static readonly LocalizedTextSet CandidateCountText = new(
        "Candidates",
        "候选",
        "候選",
        "候選"
    );
    private static readonly LocalizedTextSet TenWinLabelText = new(
        "10-win",
        "十胜",
        "十勝",
        "十勝"
    );

    public static string Title() => L.Resolve(TitleText);

    public static string Subtitle() => L.Resolve(SubtitleText);

    public static string FinalBuildRow() => L.Resolve(FinalBuildRowText);

    public static string ShopRow() => L.Resolve(ShopRowText);

    public static string BoardRow() => L.Resolve(BoardRowText);

    public static string StashRow() => L.Resolve(StashRowText);

    public static string Close() => L.Resolve(CloseText);

    public static string Previous() => L.Resolve(PrevText);

    public static string Next() => L.Resolve(NextText);

    public static string NoRun() => L.Resolve(NoRunText);

    public static string NoCandidates() => L.Resolve(NoCandidatesText);

    public static string NoRecommendation() => L.Resolve(NoRecommendationText);

    public static string EmptyShop() => L.Resolve(EmptyShopText);

    public static string EmptyBoard() => L.Resolve(EmptyBoardText);

    public static string EmptyStash() => L.Resolve(EmptyStashText);

    public static string CandidateCount(int count) => $"{L.Resolve(CandidateCountText)} {count}";

    public static string RecommendationCount(int index, int count) =>
        count <= 0 ? NoRecommendation() : $"{index + 1}/{count}";

    // Compact ten-win evidence for the status rail: run count, success rate (basis points -> percent),
    // and p75 final day. Replaces the legacy free-text "source" suffix.
    public static string RecommendationEvidence(
        int tenWinRunCount,
        int? tenWinRateBps,
        int? p75FinalDay
    )
    {
        var parts = new List<string> { $"{L.Resolve(TenWinLabelText)} {tenWinRunCount}" };
        if (tenWinRateBps.HasValue)
            parts.Add(
                $"{(tenWinRateBps.Value / 100.0).ToString("0.##", CultureInfo.InvariantCulture)}%"
            );
        if (p75FinalDay.HasValue)
            parts.Add($"D{p75FinalDay.Value}");
        return string.Join(" · ", parts);
    }

    public static string FontAtlasSample() =>
        Title()
        + Subtitle()
        + FinalBuildRow()
        + ShopRow()
        + BoardRow()
        + StashRow()
        + Close()
        + Previous()
        + Next()
        + NoRun()
        + NoCandidates()
        + NoRecommendation()
        + EmptyShop()
        + EmptyBoard()
        + EmptyStash()
        + L.Resolve(TenWinLabelText);
}
