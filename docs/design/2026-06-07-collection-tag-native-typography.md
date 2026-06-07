# CollectionPanel 标签筛选「原生 typography」改造 设计

> **Status:** Draft — 待红队评审；评审修订后回送确认再实施（repo 规则：大改造先独立评审）。
>
> 本文所有 `file:line` 引用均经 12 组独立核查 agent 对 HEAD（master `0eb392c`）逐条验证；与早期会话分析不一致处（青龙面板锚点、字体光栅语义、`LocalizationSnapshot.Tests` 覆盖范围等）以本文为准。

**Goal:** 把 CollectionPanel 标签（`ECardTag`）筛选行的**文案与配色**切换到游戏原生 tooltip typography（`KeywordIconColorConfiguration` 关键词配置 + 游戏字符串表），**彻底删除** mod 手工维护的双语标签词典 `CollectionPanelText.Tag(ECardTag)`，不保留任何 fallback 路径；顺带修复 locale 切换后 chip 文案滞留的存量 bug。渲染保持 UI Toolkit，不引入原生 chip prefab。

**Tech Stack:** C# 12 / netstandard2.1、Unity UI Toolkit、`TheBazaar.UI.Tooltips.TooltipTypography`（经 `GameInterop/` 适配器 + 反射）、`TheBazaar.Utilities.LocalizableText`、`BazaarPlusPlus.Localization`（`ChineseScriptConverter` 简繁转换）、Harmony（既有 patch，无新增）。

---

## 0 已确认决策

| # | 决策点 | 选定 |
|---|---|---|
| 1 | 标签文案/配色来源 | **游戏原生**：keyword 配置表优先，游戏字符串表次之 |
| 2 | fallback | **无**。删除 `CollectionPanelText.Tag(ECardTag)`（`CollectionPanelText.cs:157-185`）；不保留 mod 侧标签词典。依据 repo 规则「替换子系统时移除旧实现、不留 fallback 路径」 |
| 3 | 渲染层 | **UITK 不变**；不复用原生 tag chip prefab（论证见 §4.1） |
| 4 | 退化语义 | typography 未注册 / 标签无配置时输出**原生 API 自身的退化结果**（枚举名），按次解析、自动自愈（§5）——这是错误语义定义，不是旧路径 |

---

## 1 现状架构

### 1.1 数据链路

```
TCardBase.Tags (HashSet<ECardTag>)                      // 游戏静态数据
  → CollectionCardVm.Tags                               // Data/CollectionCardVm.From.cs:27
  → CollectionFilterEngine tag 检查                      // Data/CollectionFilterEngine.cs:52
      AnyTagMatch（任一命中即保留，OR 语义）               // Data/CollectionFilterEngine.cs:96-107
```

`ECardTag` 共 30 个枚举值（`decompiled/BazaarGameShared/BazaarGameShared.Domain.Core.Types/ECardTag.cs`）。`Data/CollectionCardVm.From.cs:28` 同时投影 `HiddenTags`——那是 source offer-rule 的独立通道，与本筛选行无关。

### 1.2 选项与选择逻辑

- 选项词表：`CollectionTagWhitelist.Ordered` 硬编码 24 个玩家可见标签（`Data/CollectionTagWhitelist.cs:16-44`），恰好排除 6 个机制标签（Unsellable/Unstashable/Merchant/Event/Combat/Loot）；前 `PrimaryCount = 8` 个（`:14`）为折叠态主选片。
- 选择状态：`CollectionFilterState.Tags`（`Data/CollectionFilterState.cs:21`）。`ApplySelection`（`:52-72`）重置英雄/merchant-kind/ActiveType/source-key，但**从不触碰 Tags**——标签选择跨开合存续（仅内存，不跨进程）。
- toggle 链：`CollectionPanel.cs:510-517`（移除失败则添加 → 重置滚动 → `ApplyFilters` → `RefreshView`）；每次 `RefreshView` 传 `AvailableTags = CollectionTagWhitelist.Ordered`（`CollectionPanel.cs:790`）。
- 折叠/展开：`VisibleTagOptions`（`Ui/CollectionPanelView.Filters.cs:141-158`）= 主选片 + 任何被选中但本应隐藏的标签；`RefreshTagMoreButton`（`:178-189`）渲染 More(n)/Less。

### 1.3 展示层

- 标签行：`Ui/CollectionPanelView.Tree.cs:147-151`，wrap 布局。
- chip：`CreateCompactChipButton`（`Filters.cs:332-342`）——自适应宽（minWidth 54f）、`Sizes.FontSmall`(12)、`Colors.HistoryChipBackground/HistoryChipText`；**文案仅在创建时设置**（`Filters.cs:124` 调 `CollectionPanelText.Tag(captured)`，这是全仓库唯一调用点）。
- 选中态：`RefreshChip`（`Filters.cs:600-617`）只改背景/文字/边框色，从不触碰 `.text`。
- 重建判定：`TagChipsMatch`（`Filters.cs:160-168`）只比较 `ECardTag` 序列；`TierChipsMatch`/`SizeChipsMatch`（`:66-76`、`:100-110`）只比较 key 集合——均不感知文案语言。

### 1.4 本地化链路

- `CollectionPanelText.Tag()`（`CollectionPanelText.cs:157-185`）：24 case 手写「英文 + 简体 + 台繁 + 港繁」，未覆盖枚举回退 `ToString()`；语言判定 `FormatSimple`（`:237-256`）只分「中文 / 其他」。
- 语言来源：`GameInterop/GameLanguageProvider.cs:9-22` 读 `PlayerPreferences.Data.LanguageCode`；BPP 简繁模式经 `ChineseScriptConverter.Convert`（`BazaarPlusPlus.Localization/ChineseScriptConverter.cs:461-491`，无预授变体时落入逐字 `ConvertToTraditional` + 台/港用词表）。
- locale 事件：`ChineseLocaleModeChanged` 唯一发布者是 BPP 自己的设置坞条目（`Game/Settings/ChineseLocaleModeSettingsDockEntry.cs:56`）；`CollectionPanelMount.cs:25-27` 订阅 → `NotifyLocaleChanged`（`CollectionPanel.cs:114-121`）失效 catalog 并重载。**游戏语言变化无事件**——mod 既不订阅游戏 `LocalizationService.LocaleChanged`，既有 Harmony patch `Patches/Settings/OptionsDialogLanguageRefreshPatch.cs:11-23` 刷新设置坞/键位/HistoryPanel，但不含 CollectionPanel。

## 2 现存问题（已核查）

**P1 — 非中文语言全部退化英文，且与游戏官方译名脱节。** `FormatSimple` 只分中文/其他；游戏 keyword 配置自带逐语言官方译文（§3.1），mod 手抄必然漂移。

**P2 — 词表与词典双重静态漂移。** 游戏新增 `ECardTag` 后：不进白名单则筛选器不可见；进了白名单但 `Tag()` 无 case 则显示英文枚举名。`Sources/` 的 offer-rule JSON 按枚举名解析，新增枚举值不破坏既有 JSON（已核查），漂移只发生在这两处 UI 真相源。

**P3 — 视觉语言割裂。** 游戏给每个标签配官方颜色（`KeywordIconColorConfiguration.Color`，`decompiled/TheBazaarRuntime/TheBazaar.Tooltips/KeywordIconColorConfiguration.cs:30`）、部分有图标（`IconName`，`:40`）；tooltip tag chip 还带品质横幅框。mod chip 统一灰底白字——玩家悬停卡牌看到的原生标签行（§3.3）与筛选行视觉完全对不上。

**P4 — locale 切换后 chip 文案滞留（已确认 bug）。** 完整链路核查：`ChineseLocaleModeChanged` → `NotifyLocaleChanged`（`CollectionPanel.cs:114-121`）→ `StartPanelLoad` → `RefreshView` → `view.Refresh`（`Ui/CollectionPanelView.cs:300-382`）——`Ensure*Chips` 因 key 匹配早退，`RefreshChip` 不设 text，chip 文案永不更新。细化结论：
- More/Less 按钮文案**会**刷新（`EnsureTagChips` 无条件调 `RefreshTagMoreButton`，`Filters.cs:136`）——于是出现「新语言的 More(n) 旁边挂着旧语言 chips」的割裂态；
- tag chips 在可见序列变化（展开/收起、选中主选片外标签）时**偶然自愈**；tier/size chips 因 `TierOrder`/`SizeOrder` 是静态常量（`CollectionPanel.cs:46-60`）**永不自愈**；
- 滞留面比 chips 更宽：节标题（`Tree.cs:127/132/139/149`）、Items/Skills 页签、排序按钮、包裹开关、hero chip tooltip 均只在构建时设置；Title/Subtitle/Count/来源标题/loading 文案则每次 Refresh 重设、正常localize；
- 面板关闭时切 locale 同样命中（视图存活，下次 Open 的 Refresh 仍通过 key 匹配检查）；场景切换销毁视图（`CollectionPanel.cs:411-420` → `:448-449`）是实践中唯一的自愈机制。

**P5 — Skill 页签语义噪音。** 原生标签行仅对 Item 激活（`TagRenderer.cs:22`、`CardTooltipTypeHandler.cs:147`）；mod 筛选行在 Skill 页签照常展示整套 Item 词表，引擎对两个页签同样应用 tag 过滤（`Data/CollectionFilterEngine.cs:52` 无 ActiveType 门）。

**P6 — 「玩家词汇」知识重复。** 白名单的机制标签排除（`CollectionTagWhitelist.cs:7-9` 注释自述）与游戏 keyword 配置的存在性表达同一知识（青龙以「有无 config」判可展示性，`MerchantQuickReferenceOverlay.cs:3838-3857`）。

## 3 原生标签展示逻辑（参考实现核查）

### 3.1 原生 tooltip 链 —— 游戏内唯一的原生 tag 展示（已全仓核查确认唯一性）

```
CardTooltipTypeHandler.RenderCardUI                          // decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipTypeHandler.cs:76
  → TagRenderer.Render(card, tags, cardType, tagFrame)       // TagRenderer.cs:18-30；:22 仅 Item 激活
      每个 tag → Data.TooltipTypography.ColorTag(tag.ToString())   // :27
  → TooltipTagController.InitTooltipTag(...)                 // decompiled/TheBazaarRuntime/TheBazaar/TooltipTagController.cs:30-80
      惰性实例化 _cardTagRef prefab（Addressables TMP chip），套品质 TagFrame
      尺寸文案走 new LocalizableText(cardSize).GetLocalizedText()   // :35
      跳过 IsStringValid 失败项 ⇒ 无配置的标签在原生 tooltip 中被隐藏
```

关键原生设施（全部已核查）：

| 设施 | 位置 | 行为 |
|---|---|---|
| `Data.TooltipTypography` | `decompiled/TheBazaarRuntime/TheBazaar/Data.cs:146`，注册 `:531-538` | 全局单例；`TooltipParentComponent.BuildTypography`（`TooltipParentComponent.cs:597-610`）`async void` 注册——启动加载屏任务链中执行（`AppLoader.cs:141`、`:172`，依赖 StaticData/Addressables/Localization 任务），主菜单前**事实上**已注册，但因未 await 仍须按可空处理；locale 变化时**重建为新实例**（`TooltipTypographyDataModel.cs:21-24` 每次 `new`），缓存实例引用必然过期 |
| `ColorTag(key)` | `TooltipTypography.cs:218-226` | 无配置返回 `string.Empty`；有配置返回 `<color=#HEX>官方译文</color>` 富文本——**对 UITK 无用**，本设计不取此 API |
| `GetConfiguration(string)` | `TooltipTypography.cs:518-529`，**private**，有 `ECardAttributeType` 重载（`:513`） | `OrdinalIgnoreCase` 字典查询；键含 KeywordKey、英文 `Text.Text`、注册时 locale 文案、attribute 名（`RebuildDictionary :67-94`）——按 `ECardTag.ToString()`（如 `"Weapon"`）查询可命中 |
| `KeywordIconColorConfiguration` | `KeywordIconColorConfiguration.cs` | public 类：`Color`(:30)、`LocalizableText Text`(:33)、`IconName`(:40)、`MakeAllUppercase`(:44)；`CachedLocalizedText` 是 internal 字段(:23-24) |
| `cfg.Text.GetLocalizedText()` | `decompiled/TheBazaarRuntime/TheBazaar.Utilities/LocalizableText.cs:20-32` | 按英文原文 hash 查游戏字符串表，**live** 跟随当前 locale 连接；查不到回退原文 |
| `LocalizationService` | 启动任务硬保证（`AppLoader.cs:117/169`），主菜单前必已注册 | locale 绑定在 SQLite 连接上，**不**逐次读 `PlayerPreferences`；CJK 等带字体回退的语言切换走**整程重启**路径（`OptionsDialogController.cs:574-583` → `QuitForRestart`，门控 `LocalizationService.cs:217-224`），非重启语言走 `SetLocaleAsync`（`:226-241`） |

### 3.2 第三方先例：青龙（bazaar-qinglong）速查面板

`decompiled/bazaar-qinglong/BazaarRecommend/MerchantQuickReferenceOverlay.cs` 是「原生优先、自维护兜底」的成熟样板。**锚点更正**：商人速查面板根节点锚定**右上角**（`:1555` `CreateTopRightRect`，anchors/pivot=(1,1)，`:4297-4299`），技能速查面板同（`SkillTrainerQuickReferenceOverlay.cs:1097`）；早期分析引用的 `:274-276` bottom-left 锚点属于其内嵌的 `DockedTooltipPositioner`，非面板本体。该面板作为设计参考的价值在解析链而非屏幕位置：

- 文案：`LocalizeItemTag`（`:3897-3918`）先 `new LocalizableText(tag.ToString()).GetLocalizedText()`，原生未命中才落自维护中文字典，最终枚举名。
- keyword 样式：`TryFormatGameKeyword`（`:3782-3821`）`Data.TooltipTypography` → `GetConfiguration` → `GetKeywordTranslation` → 带官方颜色（可带图标）的富文本；全程 try/catch fail-closed。
- 可展示性 = config 存在性（`:3838-3857`）。
- 注意：青龙对 `ECardTag` 的**颜色**并未取自游戏（`ItemTagLabel :3859-3872` + 自维护色表）——keyword config 主要服务 `EHiddenTag`；哪些 `ECardTag` 有 config 是资产数据，静态不可知（开放问题 §9.1）。
- 图标：`ResolveSpriteAssetForIcon`（`:4376-4425`）扫已加载 `TMP_SpriteAsset` 复用游戏图标。

### 3.3 仓库内原生复用先例

- 网格卡面即原生 `CardPreviewBase`（`Grid/CollectionCardFactory.cs:36-71`）；hover 经 `Grid/CollectionCardHoverRelay.cs:34-46` 触发**原生 tooltip**——已核查完整链（`CardPreviewBase.OnHover` → `TooltipParentComponent.ShowCardTooltipController` → `CardTooltipTypeHandler` → `TagRenderer`）：**玩家悬停收藏页卡牌时看到的标签行已经是原生渲染**。本改造让筛选行与它对齐。
- 原生 Sprite 进 UITK 背景：`HeroPortraitSpriteProvider`/`EncounterPortraitSpriteProvider`（消费侧 `Filters.cs:491-575`，async 加载 + `userData` 防陈旧守卫）。
- 卡牌标题已用原生本地化（`Data/CollectionLocalizationResolver.cs:16-31`）。
- 私有成员反射惯例：`Game/Tooltips/CardTooltipDataFactory.cs:21-49`（私有字段 FieldInfo + fail-closed 一次性告警）、`GameInterop/Encounter/InteractionFilterProbe.cs:31`（`AccessTools.Field`）、`Game/CombatReplay/Bootstrap/BootstrapManagerInitializer.cs:28-31`。惯例记录于 `docs/audits/2026-06-05-codebase-health-audit.md:376` 与 agent memory；**已知例外**：2026-05-31 CollectionPanel patches 曾显式选择 publicizer 直访（`docs/design/2026-05-31-collection-panel-design.md:218,625`）。本设计选反射（多数惯例 + 游戏更新时 fail-closed 可控）。

## 4 设计决策

### 4.1 取数据、弃 prefab

原生标签展示的价值 90% 在数据（官方译名 + 官方配色 + 配置存在性），10% 在 TMP chip 横幅贴图。后者要求：活体捕获 tooltip prefab 上的 `_cardTagRef` 引用与按品质区分的 `TagFrame`（`BaseTooltipController.cs:81`，per-tier `TooltipFrameSetting` **class**，`:25-26`、`AssignTooltipFrame :193-205`）、uGUI overlay 几何同步、自行编织点击/选中态——原生组件本身是纯展示件，拿来当筛选控件违背其设计。故渲染留在 UITK chip 体系，仅数据源原生化。

### 4.2 无 fallback 的语义

**删除**：`CollectionPanelText.Tag(ECardTag)`（`CollectionPanelText.cs:157-185`）。删除影响面已核查：全仓**唯一**编译调用点是 `Filters.cs:124`；`CollectionPanelText.cs` 不被任何测试 csproj Compile-Include；`tests/LocalizationSnapshot.Tests` 是未入库的死构建产物目录（无源码、不在 git 历史、`run.sh test` 不发现，且其历史内容只快照过 History 面板字符串）——**不构成覆盖，也不阻塞删除**。`TagHeader`/`TagMore`/`TagLess`（`CollectionPanelText.cs:106-116`）是行级 chrome，独立保留。

**退化语义 ≠ fallback**：解析链（§5）的每个失败点都退化为**原生 API 自身的行为**（`LocalizableText` 查不到返回原文；typography 未注册返回枚举名），不存在第二份 mod 词典。退化结果不缓存、按次解析，typography 注册后下一次 `Refresh` 即自愈。

**接受的代价**（评审请重点确认）：
1. 简繁台/港模式失去预授变体——原生 zh-CN 文案经 `ChineseScriptConverter.Convert(text, null, null, mode)` 逐字转换 + 台/港用词表（`ChineseScriptConverter.cs:483-490`），未映射字符原样通过（`:509`）。相对今天手写的台/港文案是**质量回退**，换取零维护。
2. 启动极早窗口（异步注册完成前）打开面板会短暂看到英文枚举名 chips，任意一次交互/Refresh 后自愈。
3. 游戏字符串表没有的语言/词条显示英文原文——与游戏自身 `LocalizableText` 行为一致。

### 4.3 私有成员访问

`GetConfiguration` 私有且有重载——反射须显式参数类型消解：

```csharp
private static readonly MethodInfo? GetConfigurationMethod = AccessTools.Method(
    typeof(TooltipTypography), "GetConfiguration", new[] { typeof(string) });
```

缓存 `MethodInfo`（静态只读），**不缓存 `TooltipTypography` 实例引用**（每次 `Data.TooltipTypography` 现读；实例在 locale 变化时整体替换，引用即缓存键，见 §5 缓存策略）。`MethodInfo` 解析失败（游戏更新改名）→ 一次性 `BppLog.Warn` + 该路径永久退化（颜色缺省、文案走 `LocalizableText`），不抛不崩——与 `CardTooltipDataFactory.LogUnavailable`（`CardTooltipDataFactory.cs:129-136`）同型。

### 4.4 颜色应用方式

- **禁用富文本路径**：`ColorTag` 输出 TMP `<color>` 标记，UITK 标签对 TMP `<font>/<sprite>/<voffset>` 不兼容，且 inline markup 会令 `style.color` 失效、破坏选中态切换。
- 颜色取 `cfg.Color`，仅作用于 `RefreshChip` **未选中分支**的文字色（替代 `Colors.HistoryChipText`）；选中分支保持 `ButtonSelectedBackground/ButtonSelectedText` 金色高亮不变（`Filters.cs:600-617`）。无配置标签用现 token 缺省色。
- `MakeAllUppercase`（`KeywordIconColorConfiguration.cs:44`）按原生语义 honor（英文大写化）。
- 对比度（深灰底上原生色的可读性）列为游戏内验证项（§9.5）。

### 4.5 两条 locale 轴

| 轴 | 触发 | 本设计行为 |
|---|---|---|
| BPP 简繁模式 | `ChineseLocaleModeChanged`（`ChineseLocaleModeSettingsDockEntry.cs:56`）→ 既有订阅链（`CollectionPanelMount.cs:25-27`） | 解析链末端套 `ChineseScriptConverter`；缓存键含 mode，事件即失效 |
| 游戏语言 | CJK 等字体回退语言**重启进程**（mod 状态全清，无滞留）；非重启语言 `SetLocaleAsync` 进程内切换 | 标签按次解析 + 缓存键含 languageCode → 下一次 `Refresh`（任意交互/开面板）自愈。**不**新增对 `OptionsDialogController.OnLanguageOptionChanged` 的 patch 扩展：该方法 `async void`（`OptionsDialogController.cs:564`），Harmony postfix 在首个 await 处即触发、可能早于 `SetLocaleAsync` 完成——既有 `OptionsDialogLanguageRefreshPatch` 已踩在这一时序上，不再加注此陷阱面 |

两套文案源（chip = 原生 locale，More/Less/标题 = mod `L`）都锚定 `PlayerPreferences.Data.LanguageCode`，叠加同一简繁转换后行内一致。

### 4.6 字体与光栅（核查更正早期假设）

- UITK 在本 Unity 版本将 legacy `Font` 转换为 `AtlasPopulationMode.Dynamic` 的 TextCore FontAsset，**按需光栅缺失字形**——实证：面板上大量从未 warmup 的中文（节标题、tier/size/tag chips、计数）正常渲染。`Font.RequestCharactersInTexture` 写入的是 legacy 自有 atlas，UITK TextCore 路径不采样——既有 warmup 调用对 UITK 渲染**正确性与性能均无效**。本设计**不为新文案添加 warmup**，也不动既有调用（超范围；两篇旧设计文档的「缺字」论断与引擎路径不符，仅在此记录漂移：`docs/design/2026-06-07-live-build-refresh-and-history-health-layout.md:173`、`2026-06-03-localization-module-extraction-design.md:112`）。
- **韩文风险**：内嵌字体是 Source Han Sans 的 **CN 子集**，简繁与日文假名有覆盖、**谚文（Hangul）完全缺失**。今天 ko 玩家看英文标签（无碍）；原生化后 ko 玩家将得到韩文字符串 → 可能 tofu。列为游戏内验证项（§9.7）；若证实 tofu，按 repo 规则走**字体路由**（追加 KR 子集/泛 CJK 字体），不得用「限制语言」之类的伪 fallback 解决。

## 5 标签解析链（最终设计）

```
ResolveTag(ECardTag tag) -> NativeTagDisplay { string Label; Color? AccentColor }
  // 主线程；每次 Refresh 经缓存调用；缓存键 = (typography 实例引用, L.CurrentLanguageCode, L.CurrentMode)
  // —— 实例引用即天然失效键（locale 变化 = 新实例，§3.1）；键变则整表清空。
  // typography == null 时的结果【不入缓存】⇒ 注册完成后自动自愈。

  1. name = tag.ToString()                       // "Weapon"，枚举名即查询键
  2. typo = Data.TooltipTypography               // 现读，可空（启动窗口 / TooltipParentComponent.OnDestroy 后）
  3. cfg  = typo 非空 ? GetConfiguration(name) : null   // 反射调用（§4.3），异常 fail-closed 视同 null
  4. cfg 非空:
       label = cfg.Text.GetLocalizedText()       // 官方译文，live 跟随游戏 locale
       if cfg.MakeAllUppercase: label = label.ToUpperInvariant()
       color = cfg.Color
     cfg 为空:
       label = new LocalizableText(name).GetLocalizedText()   // 游戏字符串表；查不到返回 name 原文
       color = null                               // try/catch：任何异常 → label = name
  5. if LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode) && L.CurrentMode != Mainland:
       label = ChineseScriptConverter.Convert(label, null, null, L.CurrentMode)
  6. return (label, color)
```

失败点全景：

| 失败点 | 触发 | 输出 | 自愈 |
|---|---|---|---|
| typo == null | 启动异步注册未完成（`TooltipParentComponent.cs:601-602` await 前）；tooltip 宿主销毁 | `LocalizableText` 路径（LocalizationService 有启动硬保证，typo 注册任务依赖它——`AppLoader.cs:172`） | 不入缓存，下次 Refresh 重试 |
| 反射解析失败 | 游戏更新改私有签名 | 文案走 `LocalizableText`，无色 | 一次性 Warn，本会话内不再试 |
| cfg miss | 该标签无 keyword 配置（原生 tooltip 对这类标签直接隐藏，筛选行**不能**隐藏——选项仍参与过滤） | `LocalizableText` 文案，无色 | 入缓存（合法状态） |
| 字符串表 miss | 冷门语言/词条 | 英文原文（`LocalizableText.cs:28-29` 自身行为） | 入缓存 |

## 6 代码结构调整

```
src/BazaarPlusPlus/
  GameInterop/TagTypography/                     # 新增（分层规则：可复用游戏运行时适配器入 GameInterop；
    NativeTagTypography.cs                       #   v2 keywords facet（EHiddenTag）将复用同一适配器——入参即 string key）
    NativeTagDisplay.cs                          # readonly struct { string Label; Color? AccentColor }
  Game/CollectionPanel/
    CollectionPanelText.cs                       # 删除 Tag(ECardTag)（:157-185）；TagHeader/TagMore/TagLess 保留
    Ui/CollectionPanelView.Filters.cs            # chip 创建/刷新消费 NativeTagTypography；Refresh 无条件重设文案（§7 步骤 3）
tests/Architecture.Tests/CoreLayeringTests.cs    # 新增 feature-scoped ratchet 测试（§8.1）
```

不动的部分：`CollectionFilterEngine`、`CollectionFilterState`、`CollectionTagWhitelist`（Phase 2 前）、`CollectionCardVm`、catalog/虚拟化全链、`Sources/` 子系统。

## 7 实现步骤

### Phase 1 — 原生 typography 接入 + 删除手工词典（本提案核心，单 PR）

1. **适配器**：`GameInterop/TagTypography/NativeTagTypography.cs` 按 §5 实现 `Resolve(ECardTag)`（内部以 string key 实现，公开 `ECardTag` 便利入口）；缓存表 + 三元缓存键 + null 结果不入缓存；反射 `MethodInfo` 静态缓存；fail-closed 一次性告警。
2. **视图接入**：`EnsureTagChips`（`Filters.cs:112-137`）创建 chip 时取 `NativeTagTypography.Resolve(tag).Label`；`Refresh`（`Ui/CollectionPanelView.cs:300-382`）的 tag chip 循环（`:336-337`）改为「重设 `.text` + 传 `AccentColor` 给 `RefreshChip` 变体」——文案随缓存键失效自动跟新，**结构性消除 P4 的标签部分**；`TagChipsMatch` 维持纯序列比较不变。
3. **同类 bug 一并修净（显式扩展范围，评审可裁剪）**：同一 Refresh 循环对 tier/size chips（`:332-335`）无条件重设 `.text`（`CollectionPanelText.Tier/Size`），节标题、Items/Skills 页签、排序按钮、包裹开关文案改为每次 Refresh 重解析——与 Title/Subtitle/Count 的既有逐次解析模式（`:305-307`）对齐。hero chip tooltip 同步重设。这是 P4 的非标签残留，机制相同、同文件、增量小。
4. **删除** `CollectionPanelText.Tag(ECardTag)`。
5. **架构测试**：`CollectionPanel_does_not_import_native_tooltip_namespaces` —— 仿 `CollectionPanel_does_not_depend_on_HistoryPanel_preview_internals`（`CoreLayeringTests.cs:77-119`）的目录扫描样式，对 `Game/CollectionPanel` 禁止 `using TheBazaar.UI.Tooltips` / `using TheBazaar.Tooltips`（当前为零，测试即过；已知盲区一并注释声明：完全限定名内联引用与 alias using 不在 `StartsWith` 检测范围）。注意这是 feature-scoped ratchet——repo 分层规则本就允许 `Game/` 引用 `TheBazaar.*`（`Game/Tooltips` 合法引用同命名空间），此测试只锁定「CollectionPanel 经 GameInterop seam 消费 typography」这一决策。

### Phase 2 — 词表去漂移（独立 PR，本文仅立项）

6. `CollectionTagWhitelist` 降级为「排序偏好 + 机制标签黑名单」；catalog 构建时收集实际出现的 `ECardTag`，白名单外的新标签自动追加到扩展区尾部（解 P2 的可见性半边；译名半边已被 Phase 1 解决）。**注意 test harness 陷阱**：`CollectionTagWhitelist.cs` 被 exe-runner 测试工程 Compile-Include（`tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj:72`），改其形状需同步动测试工程。
7. Skill 页签决策：跟随原生语义隐藏标签行（仿 Size 行 `visibility:Hidden` 占位，`Ui/CollectionPanelView.cs:356-365`），或按 Skill 卡实际携带标签收窄词表——以 Phase 2 时的 catalog 数据定。

### Phase 3 — 可选视觉增强（仅 Phase 1 游戏内验证后考虑）

8. keyword 图标：`IconName` → 青龙 `ResolveSpriteAssetForIcon` 模式取 `TMP_SpriteAsset` 中的 Sprite → 以独立 UITK 背景元素呈现（UITK 不支持 TMP `<sprite>` 标签；骨架复用 portrait provider）。
9. **不做** `TagFrame` 横幅底图：须活体捕获且语义是卡牌品质而非标签身份。

## 8 测试与验证

### 8.1 自动化

- 新架构测试（§7 步骤 5）。
- 既有测试零影响（已核查）：`CollectionPanelText.cs` 无测试 Compile-Include；`CollectionFilterEngine.Tests` 等不触碰文案层。`./run.sh build` + `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj`。注意 `./run.sh test` 退出码陷阱——grep 全量输出确认无 `Failed test projects:`。
- 适配器解析链的纯逻辑部分（缓存键失效、退化顺序）依赖游戏类型，不适合 exe-runner 单测；以游戏内验证矩阵覆盖（无覆盖率作秀测试）。

### 8.2 游戏内验证矩阵

| # | 场景 | 预期 |
|---|---|---|
| 1 | 主菜单冷启动立即开面板 | 标签 chips 可渲染（枚举名或译文），后续交互自愈为译文 |
| 2 | 局内开面板，悬停卡牌 | 筛选 chip 译名/颜色与原生 tooltip 标签行一致 |
| 3 | 游戏语言 en ↔ 非重启语言切换 | 下一次面板交互后标签即新语言 |
| 4 | BPP 简繁三模式循环（面板开着 + 关着各一轮） | chips、tier/size、节标题全部跟随；无 P4 滞留 |
| 5 | 中文下检查全部 24 个标签 | 记录哪些命中 keyword config（§9.1 数据采集） |
| 6 | 选中态对比度 | 原生色未选中文字在灰底可读，选中金色覆盖正常 |

## 9 开放问题（游戏内采证后回填本文）

1. **24 个白名单标签中哪些有 `KeywordIconColorConfiguration`**（含 zh 译文、颜色、`MakeAllUppercase`）——ScriptableObject 资产，静态不可知。采证：临时主路径探针逐个 log `GetConfiguration(name)` 命中情况（repo 规则：不建独立诊断脚手架）。
2. 游戏 zh 字符串表对 `ECardTag` 枚举名（`LocalizableText` 路径）的命中率——青龙以此为主路径，预期良好，需实证。
3. 启动窗口退化渲染与自愈的实际观感（§8.2 场景 1）。
4. `ChineseScriptConverter` 对游戏 zh-CN 标签词汇的逐字转换质量（台/港用词表面向 mod 文案语料，对游戏词汇可能欠拟合）。
5. 原生色与选中态的视觉对比度。
6. ko 语言谚文 tofu 与否（§4.6；若 tofu → 字体路由跟进项，独立 PR）。
7. `TooltipParentComponent` 是否存在 mid-session 销毁路径（影响极小——null 守卫已覆盖；低优先）。

## 10 预期效果

| 维度 | 现状 | Phase 1 后 |
|---|---|---|
| 译名正确性 | EN/手写 zh 两档，其余语言强制英文 | 全语言跟随游戏官方译名，与原生 tooltip 标签行一致 |
| 维护成本 | 游戏每加一个标签需改白名单 + `Tag()` switch 两处 | 译名零维护（词表维护待 Phase 2 归零） |
| 视觉一致性 | 灰底白字，与游戏标签视觉无关联 | 官方 keyword 配色进 chip，与悬停 tooltip 同一视觉词汇 |
| P4 滞留 bug | tag/tier/size chips、节标题等切 locale 不刷新 | 结构性消除（按次解析 + 无条件重设） |
| v2 keywords facet 杠杆 | 需再手抄一份 `EHiddenTag` 双语表 | 同一适配器以 string key 直接服务（青龙 `TryFormatGameKeyword` 已验证该 API 路径），含图标能力 |
| 代码量 | `Tag()` switch 28 行 + 维护负担 | 净增一个 ~100 行适配器，删 28 行词典；无第二真相源 |

## 11 范围边界（本提案明确不做）

- 不删除无调用者的 `CollectionPanelText.Merchant(CollectionMerchantKind)`（`CollectionPanelText.cs:187-206`）——属 v2 keywords facet PR 的既定范围（memory: collection filter v2 backlog），不顺带扩权。
- 不动 `decompiled/`、不改 `BazaarPlusPlus.csproj` 的 Publicizer 配置。
- 不动既有 `RequestCharactersInTexture` 调用（§4.6 的无效性结论留待独立清理决策）。
- 不引入对游戏语言切换的新 Harmony patch（§4.5 时序陷阱）。
- `LocalizationSnapshot.Tests` 死目录的清理是独立的仓库卫生项，不并入本 PR。
