#nullable enable
using System.Collections.Generic;
using System.Globalization;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.DealerModel;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Localized labels for the Collection panel UI and its settings-dock entry. Lookups follow
// the HistoryPanelText pattern: store one LocalizedTextSet per concept, resolve through the
// current PlayerPreferences language code, fall through to English when nothing else fits.
internal static class CollectionPanelText
{
    private static readonly LocalizedTextSet TitleText = new(
        "Card Collection",
        "卡牌图鉴",
        "卡牌圖鑑",
        "卡牌圖鑑"
    );

    private static readonly LocalizedTextSet SubtitleText = new(
        "Supported by the BazaarPlusPlus community.",
        "由 BazaarPlusPlus 玩家社区支持。",
        "由 BazaarPlusPlus 玩家社群支持。",
        "由 BazaarPlusPlus 玩家社群支持。"
    );

    private static readonly LocalizedTextSet ItemsTabText = new("Items", "物品", "物品", "物品");
    private static readonly LocalizedTextSet SkillsTabText = new("Skills", "技能", "技能", "技能");
    private static readonly LocalizedTextSet CloseText = new("Close", "关闭", "關閉", "關閉");

    private static readonly LocalizedTextSet HeroHeaderText = new("Hero", "英雄", "英雄", "英雄");
    private static readonly LocalizedTextSet DayHeaderText = new("Day", "天数", "天數", "天數");
    private static readonly LocalizedTextSet TierSizeHeaderText = new(
        "Size / Quality",
        "尺寸 / 品质",
        "尺寸 / 品質",
        "尺寸 / 品質"
    );
    private static readonly LocalizedTextSet TagHeaderText = new("Types", "类型", "類型", "類型");
    private static readonly LocalizedTextSet KeywordHeaderText = new(
        "Tags",
        "标签",
        "標籤",
        "標籤"
    );
    private static readonly LocalizedTextSet KeywordReferenceSectionText = new(
        "Related",
        "相关",
        "相關",
        "相關"
    );
    private static readonly LocalizedTextSet FacetMatchAnyText = new("Any", "任一", "任一", "任一");
    private static readonly LocalizedTextSet FacetMatchAllText = new("All", "全部", "全部", "全部");
    private static readonly LocalizedTextSet TagMatchAnyTooltipText = new(
        "Types: match cards with any selected type. Click to require all.",
        "类型：匹配任一已选类型的卡。点击切换为必须全部匹配。",
        "類型：匹配任一已選類型的卡。點擊切換為必須全部匹配。",
        "類型：匹配任一已選類型的卡。點擊切換為必須全部匹配。"
    );
    private static readonly LocalizedTextSet TagMatchAllTooltipText = new(
        "Types: require every selected type. Click to match any.",
        "类型：必须匹配所有已选类型。点击切换为任一匹配。",
        "類型：必須匹配所有已選類型。點擊切換為任一匹配。",
        "類型：必須匹配所有已選類型。點擊切換為任一匹配。"
    );
    private static readonly LocalizedTextSet KeywordMatchAnyTooltipText = new(
        "Tags: match cards with any selected tag. Click to require all.",
        "标签：匹配任一已选标签的卡。点击切换为必须全部匹配。",
        "標籤：匹配任一已選標籤的卡。點擊切換為必須全部匹配。",
        "標籤：匹配任一已選標籤的卡。點擊切換為必須全部匹配。"
    );
    private static readonly LocalizedTextSet KeywordMatchAllTooltipText = new(
        "Tags: require every selected tag. Click to match any.",
        "标签：必须匹配所有已选标签。点击切换为任一匹配。",
        "標籤：必須匹配所有已選標籤。點擊切換為任一匹配。",
        "標籤：必須匹配所有已選標籤。點擊切換為任一匹配。"
    );
    private static readonly LocalizedTextSet SortHeaderText = new("Sort", "排序", "排序", "排序");
    private static readonly LocalizedTextSet SortQualityText = new(
        "Quality",
        "品质",
        "品質",
        "品質"
    );
    private static readonly LocalizedTextSet SortSizeText = new("Size", "尺寸", "尺寸", "尺寸");
    private static readonly LocalizedTextSet MerchantHeaderText = new(
        "Merchant",
        "商人",
        "商人",
        "商人"
    );
    private static readonly LocalizedTextSet TrainerHeaderText = new(
        "Trainer",
        "训练师",
        "訓練師",
        "訓練師"
    );
    private static readonly LocalizedTextSet PackagesToggleText = new(
        "Packages",
        "包裹",
        "包裹",
        "包裹"
    );
    private static readonly LocalizedTextSet PackagesToggleTooltipText = new(
        "Show only package cards.",
        "只显示包裹卡。",
        "只顯示包裹卡。",
        "只顯示包裹卡。"
    );

    private static readonly LocalizedTextSet CatalogLoadingText = new(
        "Loading card data...",
        "正在加载卡牌数据...",
        "正在載入卡牌資料...",
        "正在載入卡牌資料..."
    );
    private static readonly LocalizedTextSet CatalogUnavailableText = new(
        "Card data is unavailable right now. Try opening from the main menu.",
        "暂时无法读取卡牌数据，请稍后或在主菜单中重试。",
        "暫時無法讀取卡牌資料，請稍後或在主選單中重試。",
        "暫時無法讀取卡牌資料，請稍後或在主選單中重試。"
    );
    private static readonly LocalizedTextSet NoMatchesText = new(
        "No cards match the current filters.",
        "没有符合当前筛选条件的卡。",
        "沒有符合目前篩選條件的卡。",
        "沒有符合目前篩選條件的卡。"
    );
    private static readonly LocalizedTextSet SourceDisclaimerText = new(
        "Source filters are inferred from card types; in-game offers remain authoritative.",
        "来源筛选基于类型推导，具体以游戏内实际为准。",
        "來源篩選基於類型推導，具體以遊戲內實際為準。",
        "來源篩選基於類型推導，具體以遊戲內實際為準。"
    );
    private static readonly LocalizedTextSet ShopProbabilityToggleLabelText = new(
        "Shop Pool",
        "商店池",
        "商店池",
        "商店池"
    );
    private static readonly LocalizedTextSet ShopProbabilityBadgeFixedText = new(
        "Fixed",
        "固定",
        "固定",
        "固定"
    );
    private static readonly LocalizedTextSet ShopProbabilityBadgeWeightsMissingText = new(
        "Ref",
        "参考",
        "參考",
        "參考"
    );
    private static readonly LocalizedTextSet ShopProbabilityBadgeEstimateText = new(
        "Est",
        "估算",
        "估算",
        "估算"
    );
    private static readonly LocalizedTextSet ShopProbabilityBadgeNativeText = new(
        "Native",
        "原生",
        "原生",
        "原生"
    );
    private static readonly LocalizedTextSet ShopProbabilityBadgeLooseText = new(
        "Loose",
        "宽松",
        "寬鬆",
        "寬鬆"
    );
    private static readonly LocalizedTextSet ShopProbabilityBadgeExplainText = new(
        "Pool",
        "池内",
        "池內",
        "池內"
    );
    private static readonly LocalizedTextSet ShopProbabilityStatusFixedText = new(
        "Fixed offer verified",
        "固定出售已验证",
        "固定出售已驗證",
        "固定出售已驗證"
    );
    private static readonly LocalizedTextSet ShopProbabilityStatusWeightsMissingText = new(
        "Reference weights unavailable",
        "参考权重不可用",
        "參考權重不可用",
        "參考權重不可用"
    );
    private static readonly LocalizedTextSet ShopProbabilityStatusEstimateText = new(
        "Reference estimate available",
        "可用参考估算",
        "可用參考估算",
        "可用參考估算"
    );
    private static readonly LocalizedTextSet ShopProbabilityStatusExplainText = new(
        "Offer-pool eligibility",
        "出售池资格",
        "出售池資格",
        "出售池資格"
    );
    private static readonly LocalizedTextSet ShopProbabilityYesText = new("Yes", "是", "是", "是");
    private static readonly LocalizedTextSet ShopProbabilityNoText = new("No", "否", "否", "否");
    private static readonly LocalizedTextSet ShopProbabilityNativeEligibleText = new(
        "Native eligible",
        "原生路径可选",
        "原生路徑可選",
        "原生路徑可選"
    );
    private static readonly LocalizedTextSet ShopProbabilityLooseEligibleText = new(
        "Loose eligible",
        "宽松路径可选",
        "寬鬆路徑可選",
        "寬鬆路徑可選"
    );
    private static readonly LocalizedTextSet ShopProbabilityDayGateText = new(
        "Day gate",
        "天数门槛",
        "天數門檻",
        "天數門檻"
    );
    private static readonly LocalizedTextSet ShopProbabilityFixedVerifiedText = new(
        "Fixed verified",
        "固定验证",
        "固定驗證",
        "固定驗證"
    );
    private static readonly LocalizedTextSet ShopProbabilityIntervalText = new(
        "Estimate interval",
        "估算区间",
        "估算區間",
        "估算區間"
    );
    private static readonly LocalizedTextSet ShopProbabilityMissingWeightsText = new(
        "No verified old-bazaar-card-dealer weights are wired for this source yet.",
        "此来源尚未接入已验证的 old-bazaar-card-dealer 权重。",
        "此來源尚未接入已驗證的 old-bazaar-card-dealer 權重。",
        "此來源尚未接入已驗證的 old-bazaar-card-dealer 權重。"
    );
    private static readonly LocalizedTextSet ShopProbabilityReferenceDisclaimerText = new(
        "Reference only: old-bazaar-card-dealer source data may be stale and is not live authoritative probability.",
        "仅供参考：old-bazaar-card-dealer 来源数据可能过期，不是实时权威概率。",
        "僅供參考：old-bazaar-card-dealer 來源資料可能過期，不是即時權威機率。",
        "僅供參考：old-bazaar-card-dealer 來源資料可能過期，不是即時權威機率。"
    );
    private static readonly LocalizedTextSet ShopProbabilitySourceText = new(
        "Source",
        "来源",
        "來源",
        "來源"
    );

    internal static string Title() => Resolve(TitleText);

    internal static string Subtitle() => Resolve(SubtitleText);

    internal static string ItemsTab() => Resolve(ItemsTabText);

    internal static string SkillsTab() => Resolve(SkillsTabText);

    internal static string Close() => Resolve(CloseText);

    internal static string HeroHeader() => Resolve(HeroHeaderText);

    internal static string DayHeader() => Resolve(DayHeaderText);

    internal static string TierSizeHeader() => Resolve(TierSizeHeaderText);

    internal static string TagHeader() => Resolve(TagHeaderText);

    internal static string KeywordHeader() => Resolve(KeywordHeaderText);

    internal static string KeywordReferenceSection() => Resolve(KeywordReferenceSectionText);

    internal static string FacetMatchMode(CollectionFacetMatchMode mode) =>
        mode == CollectionFacetMatchMode.All
            ? Resolve(FacetMatchAllText)
            : Resolve(FacetMatchAnyText);

    internal static string TagMatchModeTooltip(CollectionFacetMatchMode mode) =>
        mode == CollectionFacetMatchMode.All
            ? Resolve(TagMatchAllTooltipText)
            : Resolve(TagMatchAnyTooltipText);

    internal static string KeywordMatchModeTooltip(CollectionFacetMatchMode mode) =>
        mode == CollectionFacetMatchMode.All
            ? Resolve(KeywordMatchAllTooltipText)
            : Resolve(KeywordMatchAnyTooltipText);

    internal static string SortHeader() => Resolve(SortHeaderText);

    internal static string SortQuality() => Resolve(SortQualityText);

    internal static string SortSize() => Resolve(SortSizeText);

    internal static string SourceHeader(ECardType activeType) =>
        activeType == ECardType.Skill ? Resolve(TrainerHeaderText) : Resolve(MerchantHeaderText);

    internal static string PackagesToggle() => Resolve(PackagesToggleText);

    internal static string PackagesToggleTooltip() => Resolve(PackagesToggleTooltipText);

    internal static string CatalogLoading() => Resolve(CatalogLoadingText);

    internal static string CatalogUnavailable() => Resolve(CatalogUnavailableText);

    internal static string NoMatches() => Resolve(NoMatchesText);

    internal static string SourceDisclaimer() => Resolve(SourceDisclaimerText);

    internal static string ShopProbabilityToggleLabel(string languageCode) =>
        ShopProbabilityToggleLabelText.Resolve(languageCode, L.CurrentMode);

    internal static string ShopProbabilityBadge(CollectionDealerCardExplain explain)
    {
        return explain.State switch
        {
            CollectionDealerProbabilityState.Fixed => Resolve(ShopProbabilityBadgeFixedText),
            CollectionDealerProbabilityState.WeightsMissing => Resolve(
                ShopProbabilityBadgeWeightsMissingText
            ),
            CollectionDealerProbabilityState.Estimate => Resolve(ShopProbabilityBadgeEstimateText),
            _ => explain.NativeEligible ? Resolve(ShopProbabilityBadgeNativeText)
            : explain.LooseEligible ? Resolve(ShopProbabilityBadgeLooseText)
            : Resolve(ShopProbabilityBadgeExplainText),
        };
    }

    internal static string ShopProbabilityDrawerText(
        CollectionDealerCardExplain explain,
        EstimateBucket? bucket
    )
    {
        var status = explain.State switch
        {
            CollectionDealerProbabilityState.Fixed => Resolve(ShopProbabilityStatusFixedText),
            CollectionDealerProbabilityState.WeightsMissing => Resolve(
                ShopProbabilityStatusWeightsMissingText
            ),
            CollectionDealerProbabilityState.Estimate => Resolve(ShopProbabilityStatusEstimateText),
            _ => Resolve(ShopProbabilityStatusExplainText),
        };

        var yes = Resolve(ShopProbabilityYesText);
        var no = Resolve(ShopProbabilityNoText);
        var lines = new[]
        {
            status,
            $"{Resolve(ShopProbabilityNativeEligibleText)}: {(explain.NativeEligible ? yes : no)}",
            $"{Resolve(ShopProbabilityLooseEligibleText)}: {(explain.LooseEligible ? yes : no)}",
            $"{Resolve(ShopProbabilityDayGateText)}: {(explain.DayGatePass ? yes : no)}",
            FixedVerifiedLine(explain, yes, no),
            EstimateLine(bucket),
            MissingWeightsLine(explain),
            SourceLine(bucket),
            Resolve(ShopProbabilityReferenceDisclaimerText),
        };
        return string.Join("\n", NonEmpty(lines));
    }

    internal static string Tier(ETier tier) =>
        tier switch
        {
            ETier.Bronze => FormatSimple("Bronze", "青铜", "青銅", "青銅"),
            ETier.Silver => FormatSimple("Silver", "白银", "白銀", "白銀"),
            ETier.Gold => FormatSimple("Gold", "黄金", "黃金", "黃金"),
            ETier.Diamond => FormatSimple("Diamond", "钻石", "鑽石", "鑽石"),
            ETier.Legendary => FormatSimple("Legendary", "传说", "傳說", "傳說"),
            _ => tier.ToString(),
        };

    internal static string Size(ECardSize size) =>
        size switch
        {
            ECardSize.Small => FormatSimple("Small", "小型", "小型", "小型"),
            ECardSize.Medium => FormatSimple("Medium", "中型", "中型", "中型"),
            ECardSize.Large => FormatSimple("Large", "大型", "大型", "大型"),
            _ => size.ToString(),
        };

    // Tag labels intentionally have no entry here: chips resolve through the game's native
    // typography (GameInterop.TagTypography.NativeTagTypography), never a mod-side dictionary.

    internal static string Hero(EHero hero) =>
        hero switch
        {
            EHero.Common => FormatSimple("Common", "通用", "通用", "通用"),
            EHero.Vanessa => FormatSimple("Vanessa", "Vanessa", "Vanessa", "Vanessa"),
            EHero.Pygmalien => FormatSimple("Pygmalien", "Pygmalien", "Pygmalien", "Pygmalien"),
            EHero.Dooley => FormatSimple("Dooley", "Dooley", "Dooley", "Dooley"),
            EHero.Mak => FormatSimple("Mak", "Mak", "Mak", "Mak"),
            EHero.Jules => FormatSimple("Jules", "Jules", "Jules", "Jules"),
            EHero.Karnok => FormatSimple("Karnok", "Karnok", "Karnok", "Karnok"),
            EHero.Stelle => FormatSimple("Stelle", "Stelle", "Stelle", "Stelle"),
            _ => hero.ToString(),
        };

    internal static string MatchCount(int count)
    {
        var languageCode = L.CurrentLanguageCode;
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return ChineseScriptConverter.Convert(
                $"共 {count} 张",
                $"共 {count} 張",
                $"共 {count} 張",
                L.CurrentMode
            );
        return $"{count} cards";
    }

    private static string FixedVerifiedLine(
        CollectionDealerCardExplain explain,
        string yes,
        string no
    )
    {
        if (!explain.FixedDealVerified.HasValue)
            return string.Empty;
        return $"{Resolve(ShopProbabilityFixedVerifiedText)}: {(explain.FixedDealVerified.Value ? yes : no)}";
    }

    private static string EstimateLine(EstimateBucket? bucket)
    {
        if (bucket == null)
            return string.Empty;
        return $"{Resolve(ShopProbabilityIntervalText)}: {FormatPercent(bucket.Low)}-{FormatPercent(bucket.High)}";
    }

    private static string MissingWeightsLine(CollectionDealerCardExplain explain) =>
        explain.State == CollectionDealerProbabilityState.WeightsMissing
            ? Resolve(ShopProbabilityMissingWeightsText)
            : string.Empty;

    private static string SourceLine(EstimateBucket? bucket)
    {
        if (bucket == null)
            return string.Empty;
        return $"{Resolve(ShopProbabilitySourceText)}: {bucket.Model}, {bucket.Source}";
    }

    private static IEnumerable<string> NonEmpty(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }

    private static string FormatPercent(double value) =>
        (value * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Resolve(LocalizedTextSet set) => LocalizedTextHelpers.Resolve(set);

    private static string FormatSimple(
        string english,
        string chineseMainland,
        string chineseTaiwan,
        string chineseHongKong
    )
    {
        return LocalizedTextHelpers.FormatSimple(
            english,
            chineseMainland,
            chineseTaiwan,
            chineseHongKong
        );
    }
}
