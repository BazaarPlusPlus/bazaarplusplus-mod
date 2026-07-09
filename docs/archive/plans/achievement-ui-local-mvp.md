---
status: abandoned
archived: 2026-07-10
calibrated: 2026-07-10
---

> Status: ABANDONED — achievement UI was implemented (18bfda3f "feat: add custom achievement cards" + storm-traveler cards) then fully deleted 2026-06-30 (5ab415a5/4904d30f/b2a66a77/96bfd73e). No achievement code in src at HEAD; only PR-1 residue survives as CollectionTabKind {Items,Skills}.

# 成就 UI 本地小版本计划（不做 server / analyzers）

Status: confirmed 2026-06-11（A 路线 + 4-PR 切分已确认；本文档是本地 MVP 的执行计划）
Scope: CollectionPanel 第四个"成就"tab + 本地 catalog + 渲染 1 张占位成就卡。零网络、零 server 依赖。
参照: `docs/plans/achievement-service-design.md`（已于 2026-06-11 按 review 结论修订完毕，与本计划一致；review 为 19 代理 workflow，14 条发现——1 blocker / 7 major / 6 minor——全部经对抗复核确认）

## Review 关键结论（决定本计划形态的事实）

1. **伪造 GUID 走不了现有渲染链（blocker）。** 网格绑定第一步是
   `BppStaticDataAccess.GetCardTemplate(staticData, vm.Id)`，未知 GUID 返回 null、不产生卡面
   （`Game/CollectionPanel/Grid/CollectionCardFactory.cs:45-53`）。且失败的 bind 不会被记录，
   virtualizer 每帧重试（`Grid/CollectionGridVirtualizer.cs:185-194,341-348`），而游戏的
   `JsonGameDataManager.GetCardById` 对未知 GUID 没有负缓存——每次调用都开一条 SQLite 连接查询
   （`decompiled/TheBazaarRuntime/TheBazaar.DataManagement.Json/JsonGameDataManager.cs:73-89`）。
   伪造 GUID 一旦进入 `TryBind` 就是每帧 SQLite + Warn 风暴。
2. **"扩展现有来源枚举"不存在（major）。** tab = 游戏枚举 `ECardType`（Item/Skill）+ `PackagesOnly` 布尔
   （`Data/CollectionFilterState.cs:24,36`）；`CollectionSourceKind` 是右侧来源 chip 的 Merchant/Trainer，
   与 tab 无关。第四个 tab 是一次真实重构，不是加枚举值。
3. **"选中 → 左侧预览"与 `CollectionSelectedCardDto` 均为虚构（major）。** 面板没有点击选中、没有左侧预览面板，
   唯一交互是 hover tooltip（`Grid/CollectionCardHoverRelay.cs:16-53`）。真实的预览最小输入模型是
   `GameInterop/CardPreview/NativeCardPreviewSpec.cs:9-24`（无 Size、无 Source；DisplaySpan 由 Size 推导）。
4. **hero/day 两道默认开启的闸会把成就 tab 清空（major）。** 打开面板必种一个英雄
   （`Data/CollectionFilterState.cs:84-85`，默认 Vanessa），Heroes 为空的 VM 过不了
   `AnyHeroMatch`（`Data/CollectionFilterEngine.cs:52`）；run 内 day 闸会按天数隐藏高展示 tier
   （`Data/DayTierSchedule.cs:14-33`）。成就分支必须 `ApplyHeroFilter=false`（先例：`CollectionPanel.cs:811-813`
   的 PackagesOnly 分支）并在 Apply 调用点设 `SuppressDayGate=true`——注意 PackagesOnly 不是 day 闸先例
   （`CollectionPanel.cs:816-819` 在 PackagesOnly 下反而置 false；包裹靠引擎早退分支
   `CollectionFilterEngine.cs:42-47` 在 `:56` 的 day 检查前 continue 逃过；`SuppressDayGate=true`
   的现有先例是固定 tier 来源池）。
5. **`placeholderArtKey` 没有落点（major）。** 美术链路从模板 `ArtKey` 走 Addressables
   （`Grid/CollectionCardArtCache.cs:54-108`）；mod 自有美术缝是 `<templateGuid>.jpg` 材质替换，
   且当前只对 package 模板生效（`Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:38-40`）。
6. **好消息：`CollectionCardVm` 是纯 POCO**（`Data/CollectionCardVm.cs:28-51`），可以从纯数据伪造；
   **合成模板可行**：`TCardItem` 是 public sealed record，`CardPreviewBase.SetUp` 接受外部传入模板、
   空 Tiers 字典可容忍（`decompiled/BazaarGameShared/.../TCardItem.cs:13-23`、
   `decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewBase.cs:64-135`）；tooltip 标题回退到内联
   `TLocalizableText.Text`（`decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs:139-141`、
   `TheBazaar.Tooltips/TooltipExtensions.cs:33-44`）。
7. **catalog schema 缺陷（major，本计划一并修）：** 单语 title/description 违背本仓库 LocalizedTextSet 约定
   （`Game/CollectionPanel/Text/CollectionPanelText.cs:13-29`）；缺 `ruleParams`（7 个
   `run_perfect_win_by_hero` 行无法区分英雄）。

## 渲染路线决策

**主路线（A）：合成内存 `TCardItem` 走现有原生卡面链。**
在 `CollectionCardFactory` 加成就分支：跳过 `GetCardTemplate`，直接构造
`TCardItem { Id = catalog GUID, Type = Item, Size/StartingTier = catalog 展示字段, Tiers = 空字典,
Localization = 内联标题(按当前语言经 L 解析) }`，照常走 pool Take + `InvokeSetUpSafe`。
占位图复用 GUID-jpg 自有美术管线。理由：符合"复用原生 UI 组件与既有先例"的仓库规则，卡面观感与物品/技能 tab 一致。

**风险与回退（B）：** SetUp 对合成模板的容忍目前只在反编译源上验证过；`DTOUtils.CreateCard` 会对未知 GUID
重查并把 `_clientCard.Template` 置 null（`decompiled/TheBazaarRuntime/TheBazaar/DTOUtils.cs:64-71`），
所以 **MVP 对成就格子禁用 hover**。PR 3 的第一次进游戏验证就是 A 路线的 spike；若 SetUp 实际崩溃，
回退到 B：BPP 自绘 UI Toolkit 卡片 tile（先例：`Ui/CollectionPanelView.Filters.cs:442-445` 的
sprite-on-UITK、`Game/CardArtReplacement/BundledCustomCardArtInstaller.cs:12-30` 的内嵌 JPG）。

## 范围

**做：** tab-mode 重构、本地内嵌 catalog（1 张卡：cosmic_ray 宇宙射线）、成就 tab 网格渲染、占位图、
virtualizer 失败 bind 加固。
**不做：** server / analyzers / ModApi 路由 / AchievementClient、点击选中与详情面板（无现成交互可"沿用"）、
锁定/解锁状态与徽章（无 server 时全部视为 unknown，不渲染状态）、`hiddenUntilUnlocked` 行为、tooltip。

## PR 切分

### PR 1 — Collection tab-mode 重构（行为不变）

把 `ActiveType: ECardType` + `PackagesOnly: bool` 收敛为 mod 自有
`CollectionTabKind { Items, Packages, Skills }`（本 PR 不加 Achievements 值），仅在 grid/pool/layout
需要处映射回 `ECardType`。按仓库规则取 breaking 重构而非再加布尔。

改动点（review 已枚举）：
- `Data/CollectionFilterState.cs:24-97,120-136`（tab 状态与互斥逻辑）
- `Data/CollectionTabProfile.cs:40-55`，并把 `ShowHeroFilter` / `ShowDayFilter` 从硬编码 true 变成真实 per-tab 标志（`:28,38`）
- `Data/CollectionFilterEngine.cs:28-49`（type/package 分支）
- `CollectionPanel.cs:485-494,570-579,766-825,832-876,879-892,902-934,947-974`
- `Ui/CollectionPanelView.Tree.cs:78-95`、`Ui/CollectionPanelView.cs:364-368,460,491-495`
- `Grid/CollectionGridLayout.cs:60-75`、`Grid/CollectionGridConstants.cs:94-101`、
  `Grid/CollectionGridVirtualizer.cs:89-112`（`SetVisible(activeType)` 签名）
- `Text/CollectionPanelText.cs:27-28,98-109,179-180`
- exe-runner 测试（`tests/CollectionFilterEngine.Tests/Program.cs:214-332` 断言了两 tab+package 互斥；
  注意 csproj Compile-Include 钉路径的坑；用 `dotnet run --project` 跑，不是 `dotnet test`）

注意保留 `Open()`/`ApplySelection` 现有的"重开重置到 Item/Skill"行为（`CollectionPanel.cs:294-304`）。

验证：`./run.sh build` + 跑全部受影响 exe-runner 测试；游戏内三个 tab 行为与重构前一致。

### PR 2 — 成就 catalog 资产与加载器

- 新增 `src/BazaarPlusPlus/Data/AchievementCards/achievement-cards.json`，照
  `Data/CollectionSources/collection-sources.json` 的内嵌模式挂进 `BazaarPlusPlus.csproj`。
- Schema v1（修正原方案缺陷）：
  ```json
  {
    "schemaVersion": 1,
    "cards": [{
      "achievementId": "cosmic_ray",
      "templateId": "<随机 v4 GUID，生成后冻结>",
      "internalName": "CosmicRay",
      "title":       { "en": "Cosmic Ray", "zhHans": "宇宙射线", "zhHant": "宇宙射線" },
      "description": { "en": "Deal 9999+ damage in a single hit", "zhHans": "单次伤害达到 9999", "zhHant": "單次傷害達到 9999" },
      "category": "combat",
      "ruleKind": "combat_single_damage",
      "ruleParams": {},
      "target": 9999,
      "displayTier": "Legendary",
      "displaySize": "Medium",
      "sortKey": 10,
      "hiddenUntilUnlocked": false
    }]
  }
  ```
  变化 vs 原方案：title/description 多语（LocalizedTextSet 约定）；新增 `ruleParams`；
  **删除 `placeholderArtKey`** —— 占位图按惯例为 `<templateId>.jpg`，走 GUID-jpg 美术管线，无需字段。
- 加载器 `AchievementCardCatalog`（解析 + 校验：GUID 格式与唯一性、slug 唯一性、必填字段），
  配单测（按既有测试项目形态二选一）。
- 内嵌 1 张占位图 `<templateId>.jpg`（`BundledCustomCardArtInstaller` 先例）。

验证：单测 + `./run.sh build`。

### PR 3 — 成就 tab 渲染一张卡（A 路线 spike 在此完成）

- `CollectionTabKind` 加 `Achievements`；第四个 tab 按钮（`Ui/CollectionPanelView.Tree.cs:78-95`）；
  标签经 `CollectionPanelText` LocalizedTextSet；**把"成就"加进字体预栅格集**
  （`Ui/CollectionPanelView.cs:223-246`，CJK tofu 规则）。
- 成就 tab profile：隐藏 hero/day/tag/keyword/来源 rail；保留 tier/size 筛选
  （吃 `displayTier`/`displaySize`，机械上可用）。
- 数据：成就 tab 不走卡牌 map / offer-pool（跳过 `CollectionPanel.cs:782-801`），VM 直接来自
  `AchievementCardCatalog`；Apply 调用点 `ApplyHeroFilter=false` + 抑制 day 闸（`CollectionPanel.cs:805-820`）。
- 渲染：`CollectionCardFactory` 成就分支构造合成 `TCardItem`（见"渲染路线决策"），不触
  `BppStaticDataAccess.GetCardTemplate`。
- 美术：`CardPreviewItemArtReplacePatch.cs:38-40` 的 `IsPackageTemplate` 闸拓宽为
  "package 或 BPP 自有成就 GUID 注册表"（**不要**给合成模板打 Package hidden tag，会污染 IsPackage 分类）。
- 加固（无论如何都做）：virtualizer 对失败 bind 按当代 memoize，未知 GUID 最多一条 Warn，
  杜绝每帧 SQLite 重试（`Grid/CollectionGridVirtualizer.cs:341-348`）。
- hover：成就格子在 `CollectionCardHoverRelay` 处短路，MVP 不出 tooltip。

游戏内验证（Steam 启动，读 `BepInEx/LogOutput.log`）：
1. 第四个 tab"成就"可见可切；2. 网格出现 1 张卡：占位图 + 标题"宇宙射线"（zh）/"Cosmic Ray"（en）+ tier 框；
3. 无 "Template lookup failed" 刷屏；4. 物品/包裹/技能 tab 行为不变；5. hover 成就卡无异常日志。
若 SetUp 在真机上对合成模板崩溃 → 切 B 路线（自绘 UITK tile），PR 3 重做渲染段，PR 1/2 不受影响。

### PR 4（可选，后续）— 状态缝

`IAchievementStateSource` 接口 + 全 unknown 的本地 stub；解锁/锁定徽章按
`Grid/CollectionSourceAttributionBadge`（`CollectionGridVirtualizer.TryRealize` 绑定点）先例渲染。
等 server 阶段再接 `AchievementClient`。本 MVP 可完全不做。

## 原设计文档修订状态

review 发现的全部修订点已于 2026-06-11 应用到 `achievement-service-design.md`：tab 模型与渲染链的真实代码事实补入"代码事实"节；"扩展来源枚举 / 选中 DTO / 左侧预览 / placeholderArtKey"四处虚构改写为 tab-mode 重构 + 合成 `TCardItem` 渲染路径；schema v1 改为多语 title/description + `ruleParams` + GUID-jpg 美术约定；hero/day 闸抑制写入第四栏交付项；A/B 层归属、`GET /achievements` evidence 矛盾、`bazaar_god` 语义、catalog 生成器归属、三处标题问题、全部 stale 引用行号（含 `idx_runs_ended_at` 已删与 ghost replay-link 仅按 battle_id 的鉴权先例注记）均已修正。
