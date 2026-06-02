#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.Settings;
using TheBazaar;

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
    private static readonly LocalizedTextSet SearchPlaceholderText = new(
        "Search by name",
        "搜索名称",
        "搜尋名稱",
        "搜尋名稱"
    );

    private static readonly LocalizedTextSet HeroHeaderText = new("Hero", "英雄", "英雄", "英雄");
    private static readonly LocalizedTextSet TierHeaderText = new(
        "Quality",
        "品质",
        "品質",
        "品質"
    );
    private static readonly LocalizedTextSet SizeHeaderText = new("Size", "尺寸", "尺寸", "尺寸");
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
    private static readonly LocalizedTextSet ResetText = new("Reset", "重置", "重置", "重置");

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

    internal static string Title() => Resolve(TitleText);

    internal static string Subtitle() => Resolve(SubtitleText);

    internal static string ItemsTab() => Resolve(ItemsTabText);

    internal static string SkillsTab() => Resolve(SkillsTabText);

    internal static string Close() => Resolve(CloseText);

    internal static string SearchPlaceholder() => Resolve(SearchPlaceholderText);

    internal static string HeroHeader() => Resolve(HeroHeaderText);

    internal static string TierHeader() => Resolve(TierHeaderText);

    internal static string SizeHeader() => Resolve(SizeHeaderText);

    internal static string SortHeader() => Resolve(SortHeaderText);

    internal static string SortQuality() => Resolve(SortQualityText);

    internal static string SortSize() => Resolve(SortSizeText);

    internal static string MerchantHeader() => Resolve(MerchantHeaderText);

    internal static string SourceHeader(ECardType activeType) =>
        activeType == ECardType.Skill ? Resolve(TrainerHeaderText) : Resolve(MerchantHeaderText);

    internal static string PackagesToggle() => Resolve(PackagesToggleText);

    internal static string Reset() => Resolve(ResetText);

    internal static string CatalogLoading() => Resolve(CatalogLoadingText);

    internal static string CatalogUnavailable() => Resolve(CatalogUnavailableText);

    internal static string NoMatches() => Resolve(NoMatchesText);

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

    internal static string Merchant(CollectionMerchantKind merchant) =>
        merchant switch
        {
            CollectionMerchantKind.General => FormatSimple("General", "通用", "通用", "通用"),
            CollectionMerchantKind.Burn => FormatSimple("Burn", "燃烧", "燃燒", "燃燒"),
            CollectionMerchantKind.Poison => FormatSimple("Poison", "中毒", "中毒", "中毒"),
            CollectionMerchantKind.Freeze => FormatSimple("Freeze", "冻结", "凍結", "凍結"),
            CollectionMerchantKind.Slow => FormatSimple("Slow", "减速", "減速", "減速"),
            CollectionMerchantKind.Haste => FormatSimple("Haste", "加速", "加速", "加速"),
            CollectionMerchantKind.Speed => FormatSimple("Speed", "速度", "速度", "速度"),
            CollectionMerchantKind.Toughness => FormatSimple("Toughness", "韧性", "韌性", "韌性"),
            CollectionMerchantKind.Strength => FormatSimple("Strength", "力量", "力量", "力量"),
            CollectionMerchantKind.Heal => FormatSimple("Heal", "治疗", "治療", "治療"),
            CollectionMerchantKind.Economy => FormatSimple("Economy", "经济", "經濟", "經濟"),
            CollectionMerchantKind.Shield => FormatSimple("Shield", "护盾", "護盾", "護盾"),
            CollectionMerchantKind.Health => FormatSimple("Health", "生命", "生命", "生命"),
            CollectionMerchantKind.Joy => FormatSimple("Joy", "欢乐", "歡樂", "歡樂"),
            CollectionMerchantKind.Flying => FormatSimple("Flying", "飞行", "飛行", "飛行"),
            _ => merchant.ToString(),
        };

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
        var languageCode = GetLanguageCode();
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return BppChineseLocalization.ResolveChineseText(
                $"共 {count} 张",
                $"共 {count} 張",
                $"共 {count} 張"
            );
        return $"{count} cards";
    }

    private static string Resolve(LocalizedTextSet set) => set.Resolve(GetLanguageCode());

    private static string FormatSimple(
        string english,
        string chineseMainland,
        string chineseTaiwan,
        string chineseHongKong
    )
    {
        var languageCode = GetLanguageCode();
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return BppChineseLocalization.ResolveChineseText(
                chineseMainland,
                chineseTaiwan,
                chineseHongKong
            );
        }

        return english;
    }

    private static string GetLanguageCode()
    {
        try
        {
            return PlayerPreferences.Data.LanguageCode ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
