---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Collection Panel

游戏内 `CollectionPanel` 是一个全屏卡牌图鉴：把游戏静态卡库里的 **Item** 与 **Skill** 卡渲染成一个虚拟化网格，并按英雄 / 品质 / 尺寸 / 来源（merchant / trainer）过滤。它复用游戏自身的 `CardPreviewBase` 预制体（不自绘卡面），通过一个有界实例池 + 回收式网格把任意大小的卡库压进固定数量的存活卡片实例。可以从大厅的 Bazaar++ 设置坞按钮或 `Tab` 打开，`Escape` 关闭。

面板是 `Game` 下最大的子系统（50 个 `.cs`），分四层：`Game/CollectionPanel/`（编排）、`Data/`（卡 VM / 分类 / 过滤）、`Sources/`（merchant/trainer 来源 catalog）、`Grid/` + `Ui/`（虚拟化叠加层 + UITK 操作栏）。本文记录其 **as-shipped** 行为。

## 范围：仅 Item + Skill

卡库构建在 `CollectionCatalogBuildSession.Step` (`Data/CollectionCatalogBuildSession.cs:33`) 里逐帧扫描游戏的 `Dictionary<Guid, ITCard>`，每张卡经 `CollectionCardClassifier.Classify` (`Data/CollectionCardClassifier.cs:51`) 分类后才接受。非 `Item`/`Skill` 类型、缺失 / `Invalid` / `Placeholder` / `.mat` artKey、以及 `[DEBUG]`/`TEMPLATE` 内部名一律拒绝 (`CollectionCardClassifier.cs:63-90`)。被接受的卡投影成一个不可变 `CollectionCardVm` (`Data/CollectionCardVm.cs:12`)，过滤引擎与网格全程传同一个引用。

## 入口与生命周期

- **挂载**：`CollectionPanelMount`（一个 bespoke `IBppMountable`，非通用 `ComponentMount<T>`）在 `BppComposition.cs:97` 注册。它 `AddComponent<CollectionPanel>()` 并订阅 `ChineseLocaleModeChanged` → `CollectionPanel.NotifyLocaleChanged()`（通用 mount 无法订阅事件，这是 bespoke 的唯一理由，见 `CollectionPanelMount.cs:10-27`）。
- **dock button**：`CollectionPanelDockButtonController.Attach` 由 `Patches/Settings/BppSettingsDockPatch.cs:46` 在设置坞旁克隆出一颗按钮（图标 `BppDockButtonIconKind.CollectionPanel`）；点击触发 `CollectionPanel.OpenFromDockButton()` (`CollectionPanelDockButtonController.cs:105-108` → `CollectionPanel.cs:123`)。它**不是** `ISettingsDockEntry`，而是直接克隆原生按钮。
- **快捷键**：无修饰 `Tab` 在 `Update` 里切换面板，`Escape` 关闭面板。战斗中打开被抑制。
- **打开选择**：`Open` 经 `CollectionPanelOpenSelectionResolver.Resolve` (`CollectionPanelOpenSelectionResolver.cs:12`) 把当前 run 的英雄 + 当前 encounter / 选择项 template id 映射到一个来源条目作为初始过滤；run 内当前英雄优先。run 外则使用用户上次在 CollectionPanel 明确点击的英雄 chip（`PlayerPrefs`，账号 scope，`CollectionPanelHeroPreferenceStore.cs:22-48`），无记录或记录无效时回退到默认 Vanessa + Jay Jay (`CollectionPanel.cs:147-160`)。
- **跨 scene dispose**：`Update` 每帧 `DetectSceneChange` (`CollectionPanel.cs:411`)，scene token 一变就 `Close` + `DisposeUnityRuntime`。`DisposeUnityRuntime` (`CollectionPanel.cs:436`) 先销毁卡 GameObject（让其 patched `OnDestroy` 在缓存拆除前归还 art-cache 引用计数），再拆 virtualizer / overlay / view / 两个缓存。`OnDestroy` 额外 `InvalidateCatalog` 释放 VM 卡库缓存 (`CollectionPanel.cs:422`)。

## 过滤维度

可变选择态在 `CollectionFilterState` (`Data/CollectionFilterState.cs:16`)，纯函数 `CollectionFilterEngine.Apply` (`Data/CollectionFilterEngine.cs:15`) 据此产出有序可见集：

- **ActiveType**：`Item` / `Skill` tab（互斥），决定网格列数与 cell 尺寸。
- **Heroes**：单选英雄（`ToggleHero` 始终归一到一个英雄，`CollectionFilterState.cs:80-87`）。`SelectedHero` 仅当恰好选 1 个英雄时有值。用户明确点击英雄 chip 后，当前英雄写入 `PlayerPrefs`，用于下一次局外打开面板；run 内打开仍由当前 run 英雄和 encounter source 决定 (`CollectionPanel.cs:496-504`)。
- **Tiers**：Bronze/Silver/Gold/Diamond/Legendary 多选。
- **Sizes**：Small/Medium/Large 多选，**仅 Item tab 生效**（Skill 单一尺寸，引擎在 Skill tab 忽略此集，`CollectionFilterEngine.cs:33`；UI 在 Skill tab 隐藏该行但保留布局槽，`CollectionPanelView.cs:336-342`）。
- **来源**（merchant / trainer）：单选，保存在 `CollectionFilterState.SelectedSourceKey`。当前 tab 的 `CollectionTabProfile.SourceKind` 决定它表示 merchant 还是 trainer；Item tab 显示 merchant 来源，Skill tab 显示 trainer 来源。
- **Day**：运行日过滤，默认开启；面板打开时绑定到当前 run 天数（`CollectionPanel.cs:287-288`，`_filter.SelectedRunDay = _currentRunDay ?? DayTierSchedule.OutOfRunDay`），局外为 `DayTierSchedule.OutOfRunDay = 20`（Diamond 上限，不收窄）。引擎用 `DayTierSchedule.AllowsStartingTier(card.StartingTier, day)` 过滤（`CollectionFilterEngine.cs:49`）；UI 为顶部天数数字按钮（`CollectionPanelView.Tree.cs` CreateDayToggleButton，`CollectionPanelView.cs:329` RefreshDayToggle），点击切换是否参与过滤。
- **PackagesOnly**：Item tab 的互斥模式。开启后只显示 package cards，并临时禁用来源选择；关闭后默认排除 package cards。Skill tab 不显示该开关。
- **SortPriority**：`Quality`（先 tier 后 size）/ `Size`（先 size 后 tier），末级按 DisplayName (`CollectionFilterEngine.cs:56-89`)。

> **文本搜索已于 2026-06-03 移除。** 当前 live 代码不再有搜索框、`Search` 过滤态或去抖逻辑——`Text/CollectionPanelText.cs` 与 `Ui/` 视图里已无任何 `Search` 引用。不要再为本面板记录 / 重建搜索框；历史细节见 `docs/design/2026-06-03-collection-search-removal-sponsor-tweaks-rail-stability.md`。

历史 note：旧的 merchant-kind 过滤态已不再是当前面板入口。当前来源收窄走 `CollectionSourceCatalog` + offer-pool 路径；文档和实现都应以 `CollectionSourceKind`、`SelectedSourceKey`、`CollectionTabProfile` 为准。

## CollectionSources 子系统

`Sources/` 把一份手写的 merchant/trainer 来源表变成「选中来源 → 可见卡子集」。

- **catalog**：`CollectionSourceCatalog` (`Sources/CollectionSourceCatalog.cs:13`) 懒加载内嵌 JSON 资源（见下节 schema），建三个索引：全表、`Guid→entry`（按所有 `sourceTemplateIds`）、`sourceKey→entry`。`For(kind, hero)` 产出按 kind 过滤、按当前英雄可见的条目 (`CollectionSourceCatalog.cs:51-58`、`VisibleEntries:220`)。
- **kind → 卡类型**：`CollectionSourceOfferPoolResolver.CardTypeFor` 把 **Merchant→Item**、**Trainer→Skill** (`Sources/CollectionSourceOfferPoolResolver.cs:57-58`)；因此 Item tab 的来源选择器列 merchant，Skill tab 列 trainer (`CollectionPanel.cs:783-788`)。
- **offer-rule → offer pool**：每条目带一个 `CollectionSourceOfferRule` (`Sources/CollectionSourceOfferRule.cs:21`)。`CollectionSourceOfferPoolResolver.Matches` (`CollectionSourceOfferPoolResolver.cs:31`) 按 heroMode（`SelectedHero`/`AllHeroes`/`FixedHero`/`NeutralOnly`，`:60-85`）、startingTier（`Exact`/`AtMost`，`:87-97`）、`sizesAny`/`tagsAny`/`tagsNone`/`hiddenTagsAny`/`enchantableOnly` 把 VM 卡库筛成 offered id 集。结果按 `(source, selectedHero)` 缓存于 `CollectionSourceOfferPoolCache` (`CollectionSourceOfferPoolCache.cs:16`)；`CollectionPanel.ApplyFilters` 把该集作为 `CollectionFilterContext.OfferedCardIds` 喂进过滤引擎 (`CollectionPanel.cs:589-614`)。选了具体来源时英雄过滤通常让位给来源 offer-rule（除非在 Skill tab，`CollectionPanel.cs:742`）。
- **roster 分组**：`CollectionSourceRoster.Build` (`Sources/CollectionSourceRoster.cs:26`) 按 `(groupDisplayIndex, order, name, sourceKey)` 排序，并在 `generalist` / `tier-specialist` 组末尾标 `BreakAfter` 以在 UI 里换行。
- **hero portrait chips**：来源选择器的每个 chip 用该来源的 `PortraitTemplateId` 经 `EncounterPortraitSpriteProvider.LoadPortraitAsync` 拉肖像精灵 (`Ui/CollectionPanelView.Filters.cs:399-415`、`428-440`)；merchant/trainer 都是 `EventEncounter`，肖像走 `template.ArtKey → EncounterAssetDataSO → LoadPortraitSpriteAsync` (`GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs:43-88`)。英雄过滤 chip 则用 `HeroPortraitSpriteProvider.LoadDefaultPortraitAsync`（`CollectionManager.GetDefaultHeroSkin → SkinAssetDataSO.LoadPortraitSpriteAsync`，`GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs:30-96`）；`EHero.Common` / `Hero8` 不渲染肖像，回退到点阵 glyph (`HeroPortraitSpriteProvider.cs:21`、`CollectionPanelView.Filters.cs:303-306`)。两个 provider 各自带 `CachedSprites` + `InFlightLoads` 去重；肖像未就绪时 chip 先显示文字首字母 / glyph，加载完成再贴图 (`CollectionPanelView.Filters.cs:454-469`)。

## 虚拟化网格 + 有界实例池 + 首次加载性能

- **固定 display-case 网格**：列数按 tab 固定（Item 10 列、Skill 7 列，`Grid/CollectionGridConstants.cs:15-16`），屏宽只改 base unit 大小与水平居中、不改列数。Item cell 宽 1/2/3 unit（按 `ECardSize`）、高 2 unit；Skill 是 1×1 方块图标 (`CollectionGridConstants.cs:83-100`)。
- **回收式 virtualizer**：`CollectionGridVirtualizer` (`Grid/CollectionGridVirtualizer.cs:31`) 只实例化滚动窗口内（+ overscan）的 cell，滚动时改写已存活 cell 的 `anchoredPosition`（O(visible)），出窗的 cell 把其 `CardPreviewBase` 归还池、新进窗的从池取 (`CollectionGridVirtualizer.cs:106-184`)。冷绑定受每帧 wall-clock 预算限速（`ColdBindBudgetMs = 3f`，`CollectionGridConstants.cs:45`、`CollectionGridVirtualizer.cs:155-165`）。
- **取消竞态**：池复用意味着同一 `CardPreviewBase` 可能在上次 `SetUp` 的 `LoadArt` 仍在飞行时被重绑。virtualizer 用 generation guard + per-cell pending-return 处理：过滤 / tab 切换 bump 全局 generation，在飞行的 `SetUp` 续延因 generation 已变而 no-op，`ShowWhenReady` 在 await 后校验 generation 才翻显 (`CollectionGridVirtualizer.cs:24-33`、`:448-467`)。
- **有界实例池**：`CollectionCardPool` (`Grid/CollectionCardPool.cs:19`) 按 `(ECardType, ECardSize)` 分桶，每桶上限 30（`DefaultMaxPoolSizePerKind`，`CollectionCardPool.cs:21`），超限即销毁最旧实例。卡片从 `MonsterBoardTooltip` 的四个预制体字段经反射克隆（`NativeCardPreviewPrefabResolver`，`CollectionCardPool.cs:33-38`）；解析失败则面板保持关闭而非崩溃。每个实例打 `CollectionPanelOwnedMarker` + `CanvasGroup`（驱动逐卡淡入，`CollectionCardPool.cs:82-90`）。
- **四层缓存**：原生 `CardPreviewItem.LoadArt` 每次都直连 Addressables 拉 `CardAssetDataSO` 并新建 Material——对 1146 张 Item 会泄漏引用计数与 draw call。`CollectionItemLoadArtPatch` (`Patches/CollectionPanel/CollectionItemLoadArtPatch.cs:18`) 对带 marker 的卡改走 `CollectionCardArtCache`（L2，LRU + refcount，仅 refcount 0 才驱逐，`Grid/CollectionCardArtCache.cs:21`）与 `CollectionCardMaterialCache`（L3，按 artKey 共享 Material 复用 draw call，`Grid/CollectionCardMaterialCache.cs:21`）。`CollectionCardPreviewDestroyPatch` 在原生 `OnDestroy` 前 null 掉 `_cardMaterial` 以免共享 Material 被销毁。两缓存经 `CollectionCardCacheHost`（静态 rendezvous，patch 与 live panel 的唯一握手点，`Grid/CollectionCardCacheHost.cs:9`）install/uninstall；面板拆除时 `DisposeAll` 真正释放 (`CollectionPanel.cs:370-374`)。
- **首次加载 shell**：`LoadPanelAsync` 是逐帧协程 (`CollectionPanel.cs:573`)：先显 loading 状态 + 空可见集，再分帧构建卡库（`CatalogBuildFrameBudgetMs = 4f` 暂停判定，`CollectionPanel.cs:26`），构建完成后过滤 + 刷新。卡库经 `CollectionCatalog` 缓存（按静态数据 manager 引用失效，`Data/CollectionCatalog.cs:16-42`），二次打开直接命中缓存。loading 期间 UITK 显示一个旋转字符 + 文案的 loading label (`Ui/CollectionPanelView.cs:250`、`Ui/CollectionPanelView.Tree.cs:319-332`)；面板开关本身有 asymmetric 淡入/淡出 (`CollectionPanelView.cs:217-240`)。
- **叠加层**：原生卡片渲染在一个独立 `ScreenSpaceOverlay` canvas（`sortingOrder = 27`，`Grid/CollectionGridOverlay.cs:19`），UITK 操作栏在 `26`；UITK 每次 grid viewport 几何变化就发布像素矩形，overlay 把它套到 clip RectTransform，使卡片在同一个洞里滚动 (`CollectionGridOverlay.cs:155-182`、`CollectionPanel.cs:357-361`)。默认 polled hover（`UsePolledHover = true`，`CollectionGridConstants.cs:65`）：不挂 GraphicRaycaster，每帧轮询 `Mouse.current` 命中 cell 派发 hover，让 UITK 在 26 层无阻拦地收到所有点击 / 滚轮 (`CollectionGridVirtualizer.cs:206-291`)。

## Source Catalog Schema（`collection-sources.json` v3）

来源表是一份手写、内嵌为程序集资源的 JSON：`BazaarPlusPlus.csproj:25` 把 `Data/CollectionSources/collection-sources.json` 作为 `EmbeddedResource` 打包，运行时由 `CollectionSourceCatalog` 按资源名后缀读取 (`CollectionSourceCatalog.cs:398-416`)。

- **`ExpectedSchemaVersion = 3`**（`CollectionSourceCatalog.cs:15`）。`Build` 在 `dto.SchemaVersion != 3` 时**直接抛** `InvalidOperationException`（`CollectionSourceCatalog.cs:101-104`）——schema 与代码必须同步升版，旧版 JSON 不会被静默接受。
- **顶层** `CollectionSourceCatalogDto` (`Sources/CollectionSourceDtos.cs:7`)：
  - `schemaVersion` (int)：必须等于 3。
  - `groups` (string[])：声明分组的**显示顺序**；entry 的 `group` 必须在此声明，重复或未声明即抛 (`CollectionSourceCatalog.cs:108-133`)。当前文件分组顺序为 `generalist, tier-specialist, size-specialist, other-hero, all-hero, tag-type-specialist, trainer`。
  - `entries` (object[])：来源条目。
- **每条 entry**（`CollectionSourceEntryDto`，`CollectionSourceDtos.cs:19`，校验在 `CollectionSourceCatalog.cs:121-177`）：
  - `name` (必填)、`kind`（`Merchant`/`Trainer`）、`group`（须在 `groups` 内）、`order` (int，必填)。
  - `availableHeroes` (string[])：可见英雄白名单；空数组 = 全英雄可见 (`CollectionSourceEntry.cs:60-61`)。
  - `description`、`portraitTemplateId`（非空 GUID，且必须 ∈ `sourceTemplateIds`）、`sourceTemplateIds`（≥1 个 GUID，跨条目唯一）。
  - `offerRule`（必填，`CollectionSourceOfferRuleDto`，`CollectionSourceDtos.cs:49`）：
    - `heroMode`：`SelectedHero` / `AllHeroes` / `FixedHero` / `NeutralOnly`。`FixedHero` 必须带 `hero`；`NeutralOnly` 不许带 `hero` (`CollectionSourceCatalog.cs:249-252`)。
    - `startingTier`（可选 `{mode, tier}`，mode = `AtMost`/`Exact`）。
    - `sizesAny` / `tagsAny` / `tagsNone` / `hiddenTagsAny`（枚举字符串数组）、`enchantableOnly` (bool)。
- **sourceKey 派生**（非 JSON 字段）：`Build` 由 `(kind, name, availableHeroes)` slug 出 base key；同 base key 多条时追加 `sourceTemplateIds` 指纹去重 (`CollectionSourceCatalog.cs:180-215`)。这是过滤态 `SelectedSourceKey` 持有的稳定键；当前 tab 的 `CollectionTabProfile.SourceKind` 决定该 key 对应 merchant 还是 trainer。

当前文件：69 条 entry（47 Merchant + 22 Trainer）。代表性 entry（按实际 JSON）——固定英雄 merchant `{heroMode:"FixedHero", hero:"Dooley"}`；中立分层 merchant `Curio` `{heroMode:"NeutralOnly", startingTier:{mode:"AtMost", tier:"Bronze"}}`；分层 trainer `Adira` `{heroMode:"SelectedHero", startingTier:{mode:"AtMost", tier:"Diamond"}}`；附魔 merchant `Serafina` `{enchantableOnly:true}`。

## 关键文件

编排 / 生命周期：
- `Game/CollectionPanel/CollectionPanel.cs`、`CollectionPanelMount.cs`、`CollectionPanelDockButtonController.cs`、`CollectionPanelOpenSelectionResolver.cs`
- `Game/CollectionPanel/CollectionPanelSelectionState.cs`（在 `Data/`）、`Text/CollectionPanelText.cs`、`CollectionPanelLoadDiagnostics.cs`

数据 / 过滤：
- `Game/CollectionPanel/Data/CollectionCatalog.cs`、`CollectionCatalogBuildSession.cs`、`CollectionCardVm.cs`(+`.From.cs`)、`CollectionCardClassifier.cs`、`CollectionFilterState.cs`、`CollectionFilterEngine.cs`、`CollectionTabProfile.cs`、`CollectionHeroScope.cs`、`DayTierSchedule.cs`

来源 catalog：
- `Game/CollectionPanel/Sources/CollectionSourceCatalog.cs`、`CollectionSourceDtos.cs`、`CollectionSourceEntry.cs`、`CollectionSourceEnums.cs`、`CollectionSourceOfferRule.cs`、`CollectionSourceOfferPoolResolver.cs`、`CollectionSourceRoster.cs`
- `Game/CollectionPanel/CollectionSourceOfferPoolCache.cs`、`CollectionSourceOfferPoolCacheKey.cs`
- `Data/CollectionSources/collection-sources.json`（内嵌资源，见 `BazaarPlusPlus.csproj:25`）

网格 / 缓存：
- `Game/CollectionPanel/Grid/CollectionGridVirtualizer.cs`、`CollectionCardPool.cs`、`CollectionCardFactory.cs`、`CollectionGridConstants.cs`、`CollectionGridOverlay.cs`、`CollectionGridLayout.cs`、`CollectionGridSlotLayer.cs`、`CollectionGridPixelization.cs`
- `Game/CollectionPanel/Grid/CollectionCardArtCache.cs`、`CollectionCardMaterialCache.cs`(+`Lru`)、`CollectionCardCacheHost.cs`、`CollectionPanelOwnedMarker.cs`、`CollectionCardHoverRelay.cs`
- `Patches/CollectionPanel/CollectionItemLoadArtPatch.cs`、`CollectionCardPreviewDestroyPatch.cs`

UI / 肖像：
- `Game/CollectionPanel/Ui/CollectionPanelView.cs`、`CollectionPanelView.Tree.cs`、`CollectionPanelView.Filters.cs`
- `GameInterop/HeroPortraits/HeroPortraitSpriteProvider.cs`、`GameInterop/EncounterPortraits/EncounterPortraitSpriteProvider.cs`

---

> 本文为 CollectionPanel 的 living truth，取代散落的 `docs/design/2026-05-31-collection-panel-*.md`、`2026-06-01/02/03-collection-*.md` 设计稿（那些是阶段性设计记录，可能已过时）。
