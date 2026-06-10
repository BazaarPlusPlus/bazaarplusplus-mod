---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# CollectionPanel Lifesteal Derived Keyword Plan

Status: Draft, ready for implementation review

Date: 2026-06-10

## Problem

CollectionPanel 的关键词筛选当前只消费 `CollectionCardVm.HiddenTags`。`CollectionFilterEngine.Apply` 在关键词筛选处把 `card.HiddenTags` 和 `filter.Keywords` 交给 `MatchesFacet`（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:60`），`MatchesFacet` 再按 `KeywordMatchMode` 做 Any/All（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:105`）。

CollectionPanel 的关键词 chip 可用性也只从 `CollectionCardVm.HiddenTags` 收集。`CollectionFacetAvailability.KeywordsFor` 遍历当前 tab 的非 package 卡，然后把每张卡的 `HiddenTags` 加进 present set（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFacetAvailability.cs:42`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFacetAvailability.cs:47`）。

`CollectionCardVm.From` 现在把游戏模板的 `template.HiddenTags` 原样投影到 VM（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:29`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:37`）。`TCardBase` 确实有 `HiddenTags` 字段（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards/TCardBase.cs:29`），但 `TCardItem` 的等级属性在 `Tiers` 中（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Item/TCardItem.cs:15`），每个 `TCardTier` 的属性字典是 `Attributes`（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards/TCardTier.cs:8`）。

游戏侧把吸血作为属性处理，而不是只依赖 hidden tag。`EHiddenTag` 中有 `Lifesteal`（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/EHiddenTag.cs:27`），`ECardAttributeType` 中也有 `Lifesteal`（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/ECardAttributeType.cs:27`）。tooltip 渲染读取 `ECardAttributeType.Lifesteal`（`decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs:443`、`decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs:445`），宝石显示也通过 `CardData.Attributes[ECardAttributeType.Lifesteal] > 0` 判断吸血材质（`decompiled/TheBazaarRuntime/TheBazaar/CardGemGroupBase.cs:40`、`decompiled/TheBazaarRuntime/TheBazaar/CardGemGroupBase.cs:44`），战斗结算读取 lifesteal 属性并触发回血（`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:1541`、`decompiled/BazaarBattleService/BazaarBattleService/BazaarCardDealer.cs:1543`）。

因此，数据中存在 “tier attribute 有 `Lifesteal > 0`，但 `HiddenTags` 没有 `Lifesteal`” 的武器时，玩家能在卡面/tooltip/战斗语义里看到吸血，但 CollectionPanel 的 Lifesteal 关键词 chip 和筛选都不能命中它。

## Goal

本轮只补 Lifesteal。凡是 catalog 中的 `TCardItem` 在游戏等级属性语义上拥有正数 `ECardAttributeType.Lifesteal`，投影到 `CollectionCardVm` 后都应包含 `EHiddenTag.Lifesteal`。

这个补丁必须同时修复两个入口：`CollectionFacetAvailability.KeywordsFor` 能看到 Lifesteal chip，因为它读 VM 的 `HiddenTags`（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFacetAvailability.cs:34`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFacetAvailability.cs:47`）；`CollectionFilterEngine.Apply` 能用 Lifesteal 筛出这些卡，因为它也读 VM 的 `HiddenTags`（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:60`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:62`）。

`CollectionKeywordWhitelist` 已经把 `EHiddenTag.Lifesteal` 放在关键词白名单中（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs:13`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs:31`），所以本轮不需要改 chip 白名单顺序或文案。

`CollectionCatalogBuildSession` 是 catalog VM 的统一入口，它只接受 `TCardBase` 并调用 `CollectionCardVm.From(template, classification)`（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCatalogBuildSession.cs:49`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCatalogBuildSession.cs:62`）。因此派生 Lifesteal keyword 应放在 VM 投影路径，而不是放在 UI 渲染或 filter engine 里做临时特判。

## Non-goals

不把 Ammo、Crit、Charge、Cooldown、Gold、Value 等其他属性一起自动映射。`ECardAttributeType` 中有很多属性（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/ECardAttributeType.cs:3`），但它们和 CollectionPanel keyword 的产品语义不一定一一等价。

不修改游戏模板对象本身。`TCardBase.HiddenTags` 来自游戏静态数据（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards/TCardBase.cs:29`），本方案只改变 BPP 的 `CollectionCardVm.HiddenTags` 投影结果（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.cs:40`）。

不新增 Lifesteal source，也不修改 `collection-sources.json`。source resolver 的 `hiddenTagsAny` 也读取 `card.HiddenTags`（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:90`、`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:106`），所以 VM 补齐后未来 Lifesteal source 会自然受益，但本轮不扩大 source catalog 范围。

不改变 Day、Hero、Source、Tier、Tag、Size 的 AND 关系。`CollectionFilterEngine.Apply` 仍会在 source、hero、tier、day、tag、keyword、size 之间逐项缩窄（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:50`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:52`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:56`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:58`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:60`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:65`）。

## Target Design

新增一个纯投影 helper，例如 `CollectionDerivedKeywordFacts`，放在 `src/BazaarPlusPlus/Game/CollectionPanel/Data/`。这个 helper 属于 CollectionPanel feature 层，因为它是筛选事实和产品语义，不属于通用 game interop adapter。

目标 API：

```csharp
internal static class CollectionDerivedKeywordFacts
{
    public static IReadOnlyCollection<EHiddenTag> ProjectHiddenTags(TCardBase template);
}
```

`ProjectHiddenTags` 先保留 `template.HiddenTags` 的所有值。只有当 `template is TCardItem item` 且 `HasPositiveLifesteal(item)` 为真时，才额外加入 `EHiddenTag.Lifesteal`。

`HasPositiveLifesteal(item)` 应优先复用 `TCardItem.GetAttributeBaseValueAtTier(ECardAttributeType.Lifesteal, lookupTier)`，因为这个方法体现了游戏的 tier fallback 语义：它从 lookup tier 向下查到 `StartingTier`，找到属性即返回（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Item/TCardItem.cs:30`、`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Item/TCardItem.cs:38`、`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Item/TCardItem.cs:42`）。实现可以枚举 `Bronze / Silver / Gold / Diamond / Legendary`，只要任一 lookup tier 返回 `> 0`，就派生 `EHiddenTag.Lifesteal`。

`ProjectHiddenTags` 不应在不需要派生时复制集合。若 `template.HiddenTags` 已经包含 `EHiddenTag.Lifesteal`，直接返回原集合即可；若需要新增 Lifesteal，再创建 `HashSet<EHiddenTag>` 副本并添加派生 tag。这样保留当前无派生路径的低开销行为，也避免修改游戏模板对象。

`CollectionCardVm.From` 应把 `HiddenTags = template.HiddenTags` 改成 `HiddenTags = CollectionDerivedKeywordFacts.ProjectHiddenTags(template)`（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:29`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:37`）。这样可用 keyword、关键词筛选、source hidden-tag 匹配都消费同一份事实。

## Data Semantics

Lifesteal 的派生条件是 “item 在任意可查询 tier 上有正数 lifesteal 属性”。这个条件比 “只看 `StartingTier` 的 attributes 字典” 更贴近游戏，因为 `GetAttributeBaseValueAtTier` 明确会从 lookup tier 回退到 `StartingTier`（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Item/TCardItem.cs:30`、`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Item/TCardItem.cs:32`、`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Item/TCardItem.cs:44`）。

Lifesteal 的派生条件必须要求属性值 `> 0`。游戏 tooltip 在没有值或值为 0 时不渲染 Lifesteal tooltip（`decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs:443`、`decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs:446`），宝石显示也要求 `Attributes[ECardAttributeType.Lifesteal] > 0`（`decompiled/TheBazaarRuntime/TheBazaar/CardGemGroupBase.cs:44`、`decompiled/TheBazaarRuntime/TheBazaar/CardGemGroupBase.cs:46`）。

Skill 暂不派生 Lifesteal。当前派生 helper 只对 `TCardItem` 生效，因为 `TCardItem` 是本轮需要修的武器/物品路径（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards.Item/TCardItem.cs:13`），而 CollectionPanel 的 tab 类型仍由 `CollectionFilterState.ActiveType` 控制（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:24`）。

Package 分类不应因为派生 Lifesteal 改变。`CollectionCardClassifier.IsPackage` 现在只看原始 `template.HiddenTags`（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardClassifier.cs:58`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardClassifier.cs:59`），本轮不改变 package 判定。

## Implementation Steps

1. 新增 `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionDerivedKeywordFacts.cs`，实现 `ProjectHiddenTags` 和 `HasPositiveLifesteal`。

2. 修改 `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs`，把 `HiddenTags = template.HiddenTags` 改为调用 `CollectionDerivedKeywordFacts.ProjectHiddenTags(template)`。

3. 不修改 `CollectionFilterEngine`。它已经通过 `MatchesFacet(card.HiddenTags, filter.Keywords, filter.KeywordMatchMode)` 支持 Any/All 关键词匹配（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:60`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:105`）。

4. 不修改 `CollectionFacetAvailability`。它已经从 VM `HiddenTags` 计算可用关键词（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFacetAvailability.cs:42`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFacetAvailability.cs:47`）。

5. 不修改 `CollectionKeywordWhitelist`。`EHiddenTag.Lifesteal` 已经在白名单中（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionKeywordWhitelist.cs:31`）。

6. 更新 `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`，把新 helper、`CollectionCardVm.From.cs`、`CollectionLocalizationResolver.cs` 链进 exe-runner 测试。这个测试项目已经引用 `BazaarGameShared.dll`（`tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj:91`、`tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj:92`），可以构造 `TCardItem` 和 `TCardTier`。

7. 在 `tests/CollectionFilterEngine.Tests/Program.cs` 添加 Lifesteal 投影回归用例。现有测试已经覆盖 keyword 白名单顺序里的 `EHiddenTag.Lifesteal`（`tests/CollectionFilterEngine.Tests/Program.cs:649`、`tests/CollectionFilterEngine.Tests/Program.cs:674`），新用例应覆盖从属性派生到 VM、可用 keyword、filter result 三个结果。

## Regression Tests

新增测试一：`CollectionCardVm.From` 对没有原始 `HiddenTags.Lifesteal`、但 `Tiers[Bronze].Attributes[Lifesteal] = 100` 的 `TCardItem`，投影后的 `vm.HiddenTags` 包含 `EHiddenTag.Lifesteal`。

新增测试二：同一张模板的原始 `template.HiddenTags` 不应被修改。测试应在 `CollectionCardVm.From` 之后断言 `template.HiddenTags.Contains(EHiddenTag.Lifesteal)` 仍为 false，从而锁住 “只改 VM，不改游戏模板” 的边界。

新增测试三：把派生 VM 放进 `CollectionFacetAvailability.KeywordsFor(cards, ECardType.Item)`，返回值应包含 Lifesteal。这个断言覆盖 chip 可用性入口，因为 production view 正是把 `CollectionFacetAvailability.KeywordsFor` 的结果写进 `AvailableKeywords`（`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:828`、`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:830`、`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:861`）。

新增测试四：用 `filter.Keywords.Add(EHiddenTag.Lifesteal)` 调 `CollectionFilterEngine.Apply`，派生 VM 应被返回，不含 Lifesteal 属性的 VM 不应被返回。这个断言覆盖实际筛选入口，因为 production `ApplyFilters` 最终调用 `CollectionFilterEngine.Apply(_catalogCards, _filter, context)`（`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:767`、`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:801`）。

新增测试五：把 `KeywordMatchMode` 设为 `CollectionFacetMatchMode.All`，只选择 Lifesteal 时派生 VM 仍应命中。这个断言防止 Any/All 新逻辑和派生 hidden tag 之间出现遗漏，因为 mode 定义在 `CollectionFilterState`（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:14`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:29`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:30`）。

## Verification

先跑窄测试：

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

再跑主项目构建：

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

如果时间允许，再跑全量 test runner：

```bash
./run.sh test
```

游戏内验证必须通过 Steam 启动，保持 Steam runtime 状态：

```bash
open "steam://run/1617400"
```

游戏内验证点：

- 打开 CollectionPanel，Item tab 的关键词区应能看到 Lifesteal chip。
- 只选 Lifesteal 关键词时，至少应出现一批有吸血属性的武器。
- 同时打开 Day filter 时，较高起始等级的吸血武器仍可能被 day gate 隐藏；这是现有 day gate 行为，因为 `CollectionFilterEngine` 在 keyword 前先检查 `DayTierSchedule.AllowsStartingTier`（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:56`）。
- 选择 Hero 或 Source 时，Lifesteal 结果仍会被 hero/source 进一步缩窄；这是现有 AND 行为，因为 source gate 和 hero gate 在 keyword 前执行（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:50`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:52`）。

## Risks

如果未来有 Lifesteal 属性但没有伤害行为的 item，它也会被 Lifesteal keyword 命中。这个风险和游戏 tooltip/战斗侧一致性有关；本轮用 `ECardAttributeType.Lifesteal > 0` 作为唯一事实源，因为 tooltip、宝石和战斗都读这个属性。

如果未来把其他属性也派生为 keyword，不能直接复制 Lifesteal 规则。`CooldownMax`、`BuyPrice`、`SellPrice` 这类字段更像基础运行/经济字段，不一定等价于 CollectionPanel 的机制 keyword。

`CollectionSourceOfferPoolResolver` 也读取 `card.HiddenTags`（`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceOfferPoolResolver.cs:106`），所以 VM 派生 Lifesteal 会让任何未来 `hiddenTagsAny = Lifesteal` 的 source 自动匹配这些卡。这是预期效果，但如果 source 语义要表达“原始游戏数据显式标了 Lifesteal”，就不能复用 `card.HiddenTags`，需要另加 raw-vs-derived 区分字段。

## Suggested Follow-up

本轮实现后，可以单独开一个“机制属性派生 keyword audit”文档，逐个确认 Ammo、Crit、Freeze、Haste、Slow、Charge 是否应进入同一派生机制。不要把这个 audit 和 Lifesteal 修复混在同一个补丁里。
