# 自制卡面基座 + 成就系统 · 完整方案（本地一期）

Status: drafted 2026-06-19, decisions locked. 经 11-agent workflow（understand×5 → design×2 → judge → red-team×3）
对抗复核 + 本人独立读 decompiled/源码复核全部 load-bearing 链路（渲染、美术、tooltip、tier 筛选、本地化签名、GUID）。
取代 `docs/plans/achievement-ui-local-mvp.md` 与 `achievement-service-design.md` 的**渲染/seam 章节**；两旧文档仍是
**catalog schema、规则分层 A/B/C、server/analyzers 设计**的来源。

### 已锁定决策
1. **名字/描述：复用原生卡 tooltip**（hover 出名字+描述，§2.3）。**不做**网格常驻名字浮层。
2. **成就有 ETier**：成就 tab **保留品质（tier）筛选**——经复用原生筛选引擎实现（§2.4、A3），不是硬 bypass。
3. **A/B 路由 F2 真机 spike 决定，不预设**（合成卡 `SetUp` 是否 NRE，反编译证据指向"能"）。
4. **F3 美术合成由第二个真机闸决定**（shader-material 在只有 frame 的合成卡上是否正确合成）。
5. **GUID = 确定性 UUIDv5**，冻结命名空间 `fe4ad371-2efd-5e90-a077-eb8e8e8f51e7`（§4）。
6. **MVP 收口在 A3**（可见成就 tab + 一张本地化卡 + 占位图 + 原生 tooltip + tier 筛选）。A4 与 server/analyzers 全部后续。

Scope（已确认）：自制卡面 = 成就的**可复用渲染基座**（合成 `TCardItem` + 自定义美术 + 原生 tooltip）；成就是第一个也是
唯一消费者。不做面向玩家的任意卡换肤。本地优先，server/analyzers 仅列契约。

---

## 1. 两层模型

| 层 | 位置（新建） | 职责 | 消费者 |
|---|---|---|---|
| **基座** | `GameInterop/CustomCards/` | GUID→描述符注册表、按需合成 `TCardItem`、声明"此 GUID 有自带美术" | CollectionCardFactory（渲染）+ 美术 patch（贴图） |
| **功能** | `Game/Achievements/` | 成就 catalog → 描述符的声明式映射 | 用户 |

现有 `Game/CardArtReplacement/`（opt-in、仅 package、换已渲染卡贴图，已随包嵌 ~110 张 package 图，csproj 通配
`Resources\CustomCardArt\*.jpg:27-29`）**只被复用其 texture/material 缓存**；它的渲染入口（给已存在的卡换皮）与基座
（凭空造卡）完全不同。成就用伪造 UUIDv5，与 package 真实 GUID 共存——美术 gate 分流即可（§5 F3）。

---

## 2. 已验证代码事实

### 2.1 渲染：合成 `TCardItem` 走原生 `SetUp`（A 路）
- `CardPreviewBase.SetUp(TCardBase card, …)` 模板字段**全从入参 `card` 读，从不为模板回查 `GetCardById`**
  （`decompiled/.../TheBazaar.UI/CardPreviewBase.cs:64-117`）。frame 由 `ETier` 决定（`:138-164`，与 GUID 无关）。
- `Tiers` 空字典安全（`UpdateInstanceFromTierData` no-op `:119-136`；`TCardItem.Tiers` 默认空 `TCardItem.cs:15`）。
- `ArtKey` 空/`"Invalid"` ⇒ `LoadArt` 跳过 Addressables、整段 try/catch 不抛（`CardPreviewItem.cs:80-101`）。空 Attributes ⇒ 零 gem。
- 唯一残留 SQLite：`SetUp`→`DTOUtils.CreateCard`→`GetCardById(合成GUID)`→null、置 `_clientCard.Template=null`
  （`DTOUtils.cs:53-70`，无负缓存 `JsonGameDataManager.cs:73-89`）。**每次 bind 一次、非每帧**；`_clientCard.Template=null` 不影响渲染。

### 2.2 美术：合成卡需 shader-material，不能裸 texture
- 合成卡 ArtKey 无效 ⇒ `_cardMaterial==null`（`CardPreviewItem.cs:51-94`）；现有 gate 在 `baseMaterial==null` 早退
  （`PackageCardArtPatchGate.cs:43`）——现有 package 换贴图链对合成卡无效。
- 卡脸由 `_cardMaterialShader` 合成；裸 `RawImage.texture` 回退默认 UI shader 渲成未遮罩/错色方块。in-world 路永远把贴图
  设到带 shader 的 material（`CardArtInjector.cs:103-117`）。**基座必须用 `CardPreviewItem._cardMaterialShader`（`:16`）现建
  material、设 `mainTexture`(+`EncounterBaseMap`)、赋 `_cardImage.material`。**

### 2.3 原生 tooltip 可复用（名字 + 描述）
- tooltip 用合成模板（非空 `_cardData`）建，不是 `_clientCard.Template`（`CardTooltipData.cs:96-104`）——无 Template-NPE。
- 标题/描述 → `Localization.Title/Description.GetLocalizedText()`（`CardTooltipData.cs:141,155`）→
  `TooltipExtensions.GetLocalizedText`（`:33-44`）**miss 回退内联 `TLocalizableText.Text`** →
  `LocalizationService.TryGetText`（`:175-215`）= hash + LRU + 一次只读 SQLite 主键 Find，再 miss 返回内联文本。
  **填充合成模板 `Localization.Title.Text`/`Description.Text`（L 解析）⇒ tooltip 正确显示**，代价每次 hover 一次廉价 miss（非每帧）。
- `ValueContext(Data.Run,…)` 是 `readonly record struct`，ctor 不解引用 `Run`（`ValueContext.cs:9`）；仅描述 `{属性}` token 解析碰 Run。
  **约束：成就描述纯散文、无 token** ⇒ 主菜单 `Data.Run` 为 null 也安全。空 Attributes/Tiers/Tooltips ⇒ tooltip 仅标题+描述。

### 2.4 tier 筛选可复用筛选引擎（关键：决策 #2 的落点）
- `CollectionFilterEngine.Apply(all, filter, context)`：`card.Type != filter.ActiveType` 早退（`:40`）；
  **tier 筛选 `tierFilterCount = filter.Tiers.Count` 不受 profile 门控**（`:30,54`，`!filter.Tiers.Contains(card.StartingTier)`）；
  hero 由 `context.ApplyHeroFilter`（`:29`）、day 由 `context.SuppressDayGate`（`:36`）门控；tag/size/keyword 由 profile 门控
  （`:31-33`，空集即不生效）；`card.IsPackage` 早退（`:48`）。
- `CollectionFilterContext.ApplyHeroFilter`(默认 true)/`SuppressDayGate`(默认 false) 都是 `init` 缝（`CollectionFilterContext.cs:11,16`）。
- **结论：把成就 VM（Type=Item、IsPackage=false、带 StartingTier）喂进 `CollectionFilterEngine.Apply`，context 设
  `ApplyHeroFilter=false, SuppressDayGate=true`，ActiveType=Item ⇒ tier 链免费可用、hero/day 关、tag/size/keyword 靠空集不生效。**
- `ApplyFilters` 现状（`CollectionPanel.cs:796-819`）：`_catalogCards.Count==0`→空；否则 `CollectionQuery.Run(...)`→`SetVisible(query.Cards, ActiveType,…)`。
  成就分支应在此**之前**分流，直接调 `CollectionFilterEngine.Apply`（跳过 `CollectionQuery.Run` 的 source/offer/normalization）。
- `RefreshView` 按 `CollectionTabProfile.For(_filter.ActiveType)` 选 UI chrome（`:842`，另 `:890,:923`）——**必须按新 tab 判别式分支选 `ForAchievements()`**。
- `CollectionTabProfile`（`readonly struct`）：`ShowHeroFilter/ShowTierFilter/ShowDayFilter` 现为 `=> true` 硬编码（`:28,30,38`），
  `ShowSizeFilter/ShowTagFilter/ShowKeywordFilter` 为 ctor 字段（`:32,34,36`）。
- 默认种英雄 Vanessa（`CollectionFilterState.cs:84-85`），run 内 day 闸隐藏高 tier——故 hero/day 必须 suppress。
- 三个 exe-runner 测试用显式 `<Compile Include>` 钉路径（不 glob）：`CollectionFilterEngine.Tests`/`CollectionGridLayout.Tests`/`CollectionSourceFiltering.Tests`。

### 2.5 已确认的具体签名 / 字段（让基座代码精确）
- `LocalizedTextSet(en, zhHans, zhHant, zhHant)` 4-arg ctor（`LocalizedTextSet.cs:11-26`，TW=HK=zhHant）；`L.Resolve(set)`（`L.cs:23-25`，按当前语言/中文模式）。L 未装时 `Resolve` 抛（`L.cs:32-36`）。
- `CollectionCardVm` 是 `init` 属性 POCO（`CollectionCardVm.cs:32-51`：`Id/Type/Size/StartingTier/Heroes/Tags/HiddenTags/DisplayName/InternalName/ArtKey/IsPackage…`），`BuildVms` 可直接 `new` 构造，**不经 `From(TCardBase)`**（不引游戏卡模型）。
- 字体预栅格：`BppUiFont.RequestCharactersInTexture(chars, size, style)`（`BppUiFont.cs:21-27`，底层 `Font.RequestCharactersInTexture` 累加式），CollectionPanel 在 `CollectionPanelView.cs:179` 调一次。
- virtualizer：`TryRealize(index)`（`CollectionGridVirtualizer.cs:341`）取 vm→`_factory.TryBind(vm)`（`:345`）→`binding==null` 静默早退不记（`:347`）；`_realized` 字典（`:39`）；`BumpGeneration()=>_generation++`（`:561`）由 `SetVisible`（`:97`）与 Dispose（`:218`）调。

---

## 3. 基座 seam `GameInterop/CustomCards/`

放 `GameInterop/`：两消费者（渲染 factory + 美术 patch）共享同一适配。**绝不**把合成模板塞进 `JsonGameDataManager` card map
（经 `BppStaticDataAccess.LoadCardMap:51` 泄漏进物品/技能 tab；`GetCardMap` 整表重载 + count 膨胀可损坏全网格）。registry 是平行私有字典。

```csharp
// 一张 BPP 自有卡的唯一事实源。引用 ECardType/ECardSize/ETier/LocalizedTextSet，无 Unity/BepInEx 面。
internal sealed record BppCustomCardDescriptor
{
    public Guid Id { get; init; }                 // 确定性 UUIDv5（§4）
    public ECardType Type { get; init; }          // 成就一律 Item（须 == 引擎 ActiveType，§2.4）
    public ECardSize Size { get; init; }          // 决定 NativeCardPreviewKind + gem 布局
    public ETier StartingTier { get; init; }      // 决定 frame + tier 筛选键
    public LocalizedTextSet Title { get; init; }
    public LocalizedTextSet Description { get; init; }
    public bool HasBundledArt { get; init; }      // true ⇒ <Id>.jpg 已嵌入并落盘
    public string InternalName { get; init; }     // 仅诊断；避免 "[DEBUG]"/"TEMPLATE"
    // 无 ArtKey：BPP 卡不带游戏 ArtKey；BuildVms 打 "bpp-custom" 哨兵，永不查 Addressables。
}

internal sealed class BppCustomCardRegistry
{
    public static BppCustomCardRegistry? Current { get; set; }   // BppComposition ctor 置，Dispose 清
    public void Register(BppCustomCardDescriptor d);             // 拒绝 Guid.Empty / 重复 / 能在 BppStaticDataAccess 解析出的真实 GUID（纵深防御）
    public bool IsBppCard(Guid id);
    public bool TryGet(Guid id, out BppCustomCardDescriptor? d);
    public bool HasBundledArt(Guid id);
    public IReadOnlyList<CollectionCardVm> BuildVms();           // 直接 new CollectionCardVm，DisplayName=L.Resolve(Title) 调用时解析，ArtKey="bpp-custom"，IsPackage=false；按 sortKey 序
}

internal static class BppCustomCardTemplateFactory   // 唯一碰游戏卡类型；TryBind 里按需 lazy 合成
{
    public static TCardBase Build(BppCustomCardDescriptor d);
    // => new TCardItem {
    //      Id=d.Id, Type=d.Type, Size=d.Size, StartingTier=d.StartingTier,
    //      Tiers = new(), ArtKey = string.Empty, InternalName = d.InternalName,
    //      Localization = new TCardLocalization {
    //          Title       = new TLocalizableText { Text = L.Resolve(d.Title) },
    //          Description = new TLocalizableText { Text = L.Resolve(d.Description) } } };
    //      // 填充 Localization ⇒ 原生 tooltip（§2.3）。Heroes/Tags/HiddenTags 保留非空默认。永不设 EHiddenTag.Package。
}
```
消费点各加**一条前置早退分支**：`CollectionCardFactory.TryBind`（`:45` 前）命中 registry 用工厂合成、跳过 `GetCardTemplate`；
美术注入在 `CardPreviewItemArtReplacePatch.ApplyAfterLoad`（`:36`）对 BPP GUID 走 shader-material（§5 F3）。

---

## 4. GUID 命名空间（确定性 UUIDv5，已冻结）

- 冻结根命名空间：`BPP_CUSTOM_CARD_NAMESPACE = fe4ad371-2efd-5e90-a077-eb8e8e8f51e7`
  （= `uuidv5(NAMESPACE_DNS, "custom-card.bazaarplusplus.com")`，**生成一次、字面量提交**，勿再重算覆盖）。
- 每卡 id = `uuidv5(BPP_CUSTOM_CARD_NAMESPACE, "achievement:" + achievementId)`，可复现、永不撞真实游戏卡 id。
- 已算首批（冻进 catalog JSON，并由 A2 一致性测试断言 `templateId == uuidv5(ns, "achievement:"+slug)`）：

  | achievementId | templateId (UUIDv5, frozen) |
  |---|---|
  | `cosmic_ray` | `5351d91d-2b5c-5f44-8349-bbf334a9bbc5` |
  | `infernal_demon` | `83864e84-2fc4-5840-b1ff-04fa1eacae27` |
  | `medusa_kiss` | `f4623110-2d81-53d6-8f50-9d4df6481b7d` |

- 美术文件名 = `<templateId>.jpg`（cosmic_ray = `5351d91d-2b5c-5f44-8349-bbf334a9bbc5.jpg`）。
- `BppCustomCardRegistry.Register` 纵深防御：拒绝任何 `BppStaticDataAccess.GetCardTemplate(staticData, id) != null` 的 id（撞真实卡即抛）。

---

## 5. 基座 PR

### F1 — 基座结构（纯新增，零行为变化）
- 新建 `GameInterop/CustomCards/` 三件套（§3）。`BppComposition` ctor 在注册 `CardArtReplacementFeature`（`BppComposition.cs:93`）
  **之前**置 `BppCustomCardRegistry.Current = new()`，`Dispose` 清。无消费者、无注册卡。
- 不变量（文档化）：`BuildVms`/`Build` 不在 `L.Install`（`Plugin.cs`）前调用；可在 L 未装时回退 `Title.English` 优雅降级。
- **验证**：`./run.sh build` + `tests/BppCustomCard.Tests`（exe-runner，引游戏 DLL）：Register/TryGet/IsBppCard 往返；空/重复/撞真实 GUID 抛；
  `BuildVms` 产 `ArtKey=="bpp-custom"`、`IsPackage==false`、`Type/Size/StartingTier` 透传；`Build` 产空 Tiers + 非空 Heroes/Tags + 填充 Localization；
  UUIDv5 复现断言。`dotnet run --project`。

### F2 — 接渲染 + virtualizer 加固（**A 路 spike 闸**）
1. `CollectionCardFactory.TryBind` 加前置分支（§3）。
2. **无条件**给 `CollectionGridVirtualizer` 加失败 bind memoize：`HashSet<Guid> _failedBindGuids`；`TryRealize`（`:341`）`TryBind` 前
   `if (_failedBindGuids.Contains(vm.Id)) return;`，`binding==null` 时**仅当永久失败**才记入，`BumpGeneration`（`:561`）清空。
   - **永久 vs 瞬时（必做）**：`TryBind` 返回 null 既因未知 GUID（永久）也因 `staticData==null`（瞬时未就绪，`CollectionCardFactory.cs:41-43`）。
     让 `TryBind` 返回三态（bound/hard-miss/not-ready），**只 memoize hard-miss**；否则真卡首帧撞数据未就绪会被永久变空格。BPP GUID 在 staticData 前短路，不受影响。
     加测试：`staticData==null` 时失败的 GUID 在就绪后能重试成功。
- **spike 验证**：临时在 BppComposition 注册一个一次性描述符并塞进可见集，Steam 启动（`open steam://run/1617400`）读 `BepInEx/LogOutput.log`。
  PASS = 合成卡 frame 渲出（空脸）、无 "Template lookup failed" 刷屏、故意不可解析 GUID 仅一条 Warn、零 gem 残留。
  **`SetUp` 对合成模板 NRE → 停 A 路、转 B 路（§7）。** 合并前移除临时注册。

### F3 — BPP 卡美术注入（shader-material，**第二个真机闸**）
- gate：`PackageCardArtPatchGate` 两方法各加前置分支，键 **`HasBundledArt(id)`**（非 `IsBppCard`），命中跳过 toggle + IsPackage，直达 `CardArtReplacementFeature`。
- **注入在 `CardPreviewItemArtReplacePatch.ApplyAfterLoad`（`:36`）**：对 BPP GUID 用 `CardPreviewItem._cardMaterialShader`（`:16`）现建 material，
  `TryGetTexture` 的 `Texture2D` 设 `mainTexture`(+`EncounterBaseMap`，镜像 `CardArtInjector.cs:109-115`)，赋 `instance._cardImage.material`。**不要**裸 `RawImage.texture`。
- 注意 `Show(false)`/`OnDestroy` 置 `_cardImage.material=null`（`CardPreviewBase.cs:102`）；确认贴图熬过 pool Return/Take 或每次 postfix 重贴。
- in-world `TryGetReplacementTexture`/`ItemVisualsArtReplacePatch` **不动**；现有 package 路（toggle）逐字节不变。新图丢 `Resources/CustomCardArt/<Id>.jpg`，通配自动嵌入，无需改 csproj。
- **真机验证**：注册带 `HasBundledArt=true` 的一次性描述符 + jpg，**package toggle 关闭**下确认自定义卡脸正确合成；真 package 卡 toggle 关闭下不变。
  **shader-material 仍合成不对 → 基座级发现，可能逼 B 路。**

---

## 6. 成就 PR

### A1 — Collection tab 模型重构 `CollectionTabKind`（行为不变，**破坏式 + 升主版本**）
- `ECardType ActiveType + bool PackagesOnly` → mod 自有 `CollectionTabKind { Items, Packages, Skills }`（本 PR 不加 Achievements），
  仅在 grid/pool/layout/engine 需要处映射回 `ECardType`（Items/Packages→Item，Skills→Skill）。
- `CollectionTabProfile` 的 `ShowHeroFilter/ShowDayFilter/ShowTierFilter` 从 `=> true` 提升为 ctor 字段（三现有 tab 默认 true，不变行为），
  hero 行 + day toggle 渲染 gate 到 flag（现无条件渲染，`CollectionPanelView.cs:346-385`）。
- `Directory.Build.props` `BppVersion 4.3.0 → 5.0.0`。
- **BLOCKER（必做）**：新增 `CollectionTabKind.cs` 被被 pin 的源引用，**同 PR 把它加进三个测试 csproj 的 `<Compile Include>`**，否则三测试项目静默编译失败。不移动被 pin 文件。
- **验证**：`./run.sh build` + 三 exe-runner `dotnet run --project`，grep "Failed test projects:"；游戏内三 tab 行为与重构前一致。

### A2 — 内嵌成就 catalog + 加载器 + 描述符映射
- `Data/AchievementCards/achievement-cards.json`（schema v1，照 `collection-sources.json` 嵌入）。
- `Game/Achievements/AchievementCardCatalog`（校验 GUID 格式+唯一、slug 唯一、必填、**UUIDv5 一致性**）。
- `Game/Achievements/AchievementCardDescriptorMapper`：每行 → `BppCustomCardDescriptor`；`title/desc` 经 `LocalizedTextSet(en, zhHans, zhHant, zhHant)`
  4-arg ctor（`zhHans` 非空 ⇒ `zhHant` 必非空，否则 2-arg 静默简→繁转写错术语）；`HasBundledArt` 由 `<templateId>.jpg` 存在推。
- BppComposition ctor（F1 置 Current 后）`Register` 全部描述符。先发**一张** cosmic_ray + 其 jpg（§8）。完整 catalog 初稿见 `achievement-service-design.md`。
- **验证**：catalog 单测（解析、重复/缺字段拒、UUIDv5 断言）+ mapper 单测（Id 对得上、Title 在 zh/en 解析）。

### A3 — 成就 tab 渲染 + 原生 tooltip + tier 筛选（**首次成就形态真机验收，MVP 收口**）
- `CollectionTabKind.Achievements` + 第四 tab 按钮（`CollectionPanelView.Tree.cs:95` 后）+ `SetAchievementsTab` 命令；该命令把 `ActiveType=Item`、`PackagesOnly=false`、
  **清空 `Heroes/Tags/Keywords/Sizes`（保留/重置 `Tiers`）**——保证引擎内部 `For(Item)` 的 tag/size 门控因空集不生效（见陷阱清单）。
- **BLOCKER（RefreshView）**：`RefreshView`（`CollectionPanel.cs:842`）按 tab 判别式分支选 `CollectionTabProfile.ForAchievements()`
  （`ShowHeroFilter=false, ShowDayFilter=false, ShowSizeFilter=false, ShowTagFilter=false, ShowKeywordFilter=false, ShowTierFilter=true`），
  并 guard `AvailableSourcesFor` 与 source-kind 查找（`:890,:923`）不在成就 tab 跑；Packages 按钮隐藏。
- **tier 筛选（决策 #2）**：`ApplyFilters` 在 `_catalogCards.Count` 判断**之前**加成就分支：
  ```csharp
  var vms = BppCustomCardRegistry.Current?.BuildVms() ?? Array.Empty<CollectionCardVm>();
  var ordered = CollectionFilterEngine.Apply(vms, _filter,
      new CollectionFilterContext { ApplyHeroFilter = false, SuppressDayGate = true });
  _virtualizer.SetVisible(ordered, ECardType.Item);
  ResetVisibleScroll(); return;
  ```
  整段绕过 `CollectionQuery.Run`（source/offer/normalization 无关）；tier 链免费可用、hero/day 关。
- **名字/描述：复用原生 tooltip（§2.3）。** 合成模板 `Localization` 已由工厂填充；hover 出原生卡 tooltip 显示名字+描述，与物品卡同套交互。**不短路 hover。**
- **CJK 预栅格**：把"成就" tab 标签 + 所有卡标题按 **en+zhHans+zhHant 三套**全量喂进 `BppUiFont.RequestCharactersInTexture`
  （遍历 registry 描述符 × 三 locale/模式，不止当前语言，否则切繁体 tofu），或监听 `LocalizationService.LocaleChanged`（`:71`）重栅格。
- **验证**：四 tab 切换无 tofu；网格出 cosmic_ray（占位图 + tier 框）；**hover 出原生 tooltip 显示 宇宙射线/Cosmic Ray + 描述、无 NPE/刷屏**；
  tier 行筛选成就生效；物品/包裹/技能 tab 不变；切语言重 realize → tooltip 文本 zh↔en 翻转。

### A4（后续，本期不做）— 解锁状态徽章
server 阶段：`IAchievementStateSource` + locked/unlocked/进度徽章，照 `CollectionSourceAttributionBadge`
（`CollectionGridVirtualizer.TryRealize:362`）先例渲在格子角。描述符是卡**形态**事实源、解锁**状态**正交。**无网格常驻名字浮层**（决策 #1）。

---

## 7. 时序与回退

```
F1 结构（单测）
 └ F2 接渲染 + virtualizer 加固    ══ A 路 spike 闸（真机：合成卡渲出？）══
     ├ PASS → F3 美术 shader-material   ══ 美术真机闸（卡脸合成对？）══
     │          → A1 tab 重构（破坏式，4.3.0→5.0.0，+3 csproj）
     │          → A2 catalog + mapper + 注册（cosmic_ray 一张）
     │          → A3 成就 tab + 原生 tooltip + tier 筛选   ← 一期收口
     └ FAIL（SetUp 对合成模板 NRE）→ B 路：成就格用 BPP 自绘 UITK tile。
                F1 描述符/registry、A2 catalog→描述符映射、A3 的 tab/筛选骨架**不变**；只 F2 渲染分支 + F3 美术段改写为 UITK tile renderer。
```
A1 可与 F3 并行（文件不相交），A3 依赖二者。合并 A2 前移除 F2/F3 一次性注册——真卡只从 A2 来。

---

## 8. cosmic_ray 首卡（具体 artifact）

`achievement-cards.json`（A2 内嵌）：
```json
{
  "schemaVersion": 1,
  "cards": [{
    "achievementId": "cosmic_ray",
    "templateId":    "5351d91d-2b5c-5f44-8349-bbf334a9bbc5",
    "internalName":  "CosmicRay",
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
- 美术：`src/BazaarPlusPlus/Resources/CustomCardArt/5351d91d-2b5c-5f44-8349-bbf334a9bbc5.jpg`（通配自动嵌入）。
- 映射：`Type=Item`、`Size=Medium`、`StartingTier=Legendary`、`Title/Description` 经 4-arg `LocalizedTextSet`（TW=HK=zhHant）。

---

## 9. 验证矩阵（编译期确定 vs 真机闸）

| 项 | 状态 | 依据 / 闸 |
|---|---|---|
| 合成模板字段全从入参读、frame 按 tier、空 Tiers 安全、LoadArt 不抛 | **编译期确定** | §2.1（decompiled 已读） |
| `_clientCard.Template=null` 不影响渲染、tooltip 用合成模板 | **编译期确定** | §2.1/2.3 |
| 原生 tooltip 经内联 `.Text` 回退显示、miss 非每帧 | **编译期确定** | §2.3（TooltipExtensions/LocalizationService 已读） |
| tier 筛选经引擎免费可用、hero/day 可 suppress | **编译期确定** | §2.4（引擎已读） |
| UUIDv5 可复现、不撞真实卡 | **编译期确定** | §4（已算） |
| `SetUp` 对合成模板**运行时**不 NRE（gem init、frame 实例化等） | **F2 真机闸** | 反编译指向"能"，仅真机定 |
| shader-material 在只有 frame 的合成卡上**正确合成**卡脸 | **F3 真机闸** | 唯一美术不确定项 |
| 贴图熬过 pool Return/Take（`_cardImage.material=null` on hide） | **F3 真机闸** | `CardPreviewBase.cs:102` |

---

## 10. server / analyzers 后续契约（仅契约，不实现）
- `bazaarplusplus-server` 加 `GET /achievements?player_account_id=`；字段 `achievementId/unlocked/unlockedAtUtc` 为稳定 wire 契约（归 server `docs/api-reference.md`）；内部写走 Bearer endpoint。
- mod `ModApi` 加 `AchievementClient`，状态投进 A4 `IAchievementStateSource`。
- `analyzers` 加 achievements stage，发 `analyzer-v4/mod/achievements.json`。
- 规则分层 A/B/C、D1 表、canonical event key、幂等、R2 retention——见 `achievement-service-design.md`。

---

## 11. 关键陷阱清单（执行逐条核对）
- [ ] **不**把合成模板塞进 `JsonGameDataManager` card map（泄漏 + 整表重载损坏全网格）。
- [ ] **不**给合成模板打 `EHiddenTag.Package`（污染 IsPackage / 包裹 tab）。
- [ ] 美术注入用 **shader-material**，非裸 `RawImage.texture`。
- [ ] virtualizer memoize **只记 hard-miss**，瞬时 `staticData==null` 不记（TryBind 三态）。
- [ ] gate 分支键 **`HasBundledArt(id)`**，非 `IsBppCard`。
- [ ] `RefreshView` 按 **tab 判别式**选 `ForAchievements()`，非 `ActiveType`。
- [ ] 新 `CollectionTabKind.cs` **同 PR 加进三个测试 csproj 的 Compile-Include**。
- [ ] **成就 tab 上 `Heroes/Tags/Keywords/Sizes` 必须为空**（`SetAchievementsTab` 清），否则引擎内部 `For(Item)` 会按它们筛。
- [ ] 成就描述**纯散文、无 `{属性}` token**（否则碰 null `Data.Run`）。
- [ ] `LocalizedTextSet` 用 **4-arg ctor**、TW=HK=zhHant；`zhHans` 非空 ⇒ `zhHant` 必非空。
- [ ] CJK 预栅格按 **三套 locale 全量**喂，非当前语言。
- [ ] 合成模板 `Localization` **必须填充**（否则 tooltip 无名字）。
- [ ] `BuildVms`/`Build` 不在 `L.Install` 前调用。
- [ ] 现有 package 换贴图路（toggle）与 in-world 美术 patch **不动**。
- [ ] 成就 VM `Type=Item`（须 == 引擎 `ActiveType=Item`）、`IsPackage=false`。
