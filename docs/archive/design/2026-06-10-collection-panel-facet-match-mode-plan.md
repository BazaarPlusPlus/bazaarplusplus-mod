---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# CollectionPanel tag/keyword 组内匹配模式计划

Status: Draft. This is an implementation plan only; no runtime behavior has been changed yet.

Date: 2026-06-10

## 目标

在 CollectionPanel 的 tag 与 keyword 筛选 header 上增加一个小开关，让用户决定当前组内多个 chip 之间是“任一命中”还是“全部命中”。

目标语义：

- Tag 组内：`Any` 表示卡牌带有任意一个已选 `ECardTag` 即可；`All` 表示卡牌必须同时带有所有已选 `ECardTag`。
- Keyword 组内：`Any` 表示卡牌带有任意一个已选 `EHiddenTag` 即可；`All` 表示卡牌必须同时带有所有已选 `EHiddenTag`。
- Tag 组和 keyword 组之间继续保持 AND。也就是说，若 tag 与 keyword 都有选中项，卡牌必须同时满足 tag 组条件和 keyword 组条件。
- 默认模式为 `Any`，保持现有行为。

不做：

- 不改变 source / hero / tier / size / day / package 的组合语义。
- 不改变 tag / keyword 白名单。
- 不新增独立本地化 JSON、资源表或外部文案系统。
- 不把 mode 持久化到配置文件；第一版让它跟随当前 `CollectionFilterState` 的面板内 sticky 状态。

## 当前代码事实

当前 `CollectionFilterEngine` 已经把 tag 和 keyword 当成两个独立 facet 逐步过滤：先算 `tagFilterCount` / `keywordFilterCount`，再分别执行 tag gate 和 keyword gate（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:31`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:58`、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:60`）。因此两个 facet 之间已经是 AND。

组内现在都是 OR：`AnyTagMatch` 遍历卡牌自己的 `Tags`，任一值存在于 `filter.Tags` 就返回 true（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:102`）；`AnyKeywordMatch` 对 `HiddenTags` 使用同样逻辑（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:115`）。

现有测试也锁定了这个行为：多 skill keyword 是 OR（`tests/CollectionFilterEngine.Tests/Program.cs:414`），多 item tag 是 OR（`tests/CollectionFilterEngine.Tests/Program.cs:431`），多 item keyword 是 OR（`tests/CollectionFilterEngine.Tests/Program.cs:471`），tag 与 keyword 之间是 AND（`tests/CollectionFilterEngine.Tests/Program.cs:491`）。

筛选状态当前存在 `CollectionFilterState`：它保存 `ActiveType`、`Heroes`、`Tiers`、`Tags`、`Keywords`、`Sizes`、source、`PackagesOnly`、day 和排序，但没有组内匹配模式字段（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:18`）。

UI 当前的 tag / keyword header 是 `CreateFilterSection` 创建出来的单个 `Label`：tag 区创建于 `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:173`，keyword 区创建于 `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:185`；helper 只添加 label 和 chip row（`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:273`）。

View 的状态传播是直接 callback：tag chip 点击修改 `_filter.Tags` 后 `ApplyFilters()` + `RefreshView()`（`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:532`）；keyword chip 点击同理（`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:540`）。新增 mode 开关应该复用同一条路径。

`CollectionPanelViewModel` 当前只带选中的 tag / keyword 集合，没有 mode 字段（`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:19`）。View 每次 `Refresh` 会重算 chrome 文案与 chip 状态（`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:315`、`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:338`、`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:364`、`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:370`）。

Item / Skill 上哪些 facet 可见由 `CollectionTabProfile.For` 决定：Skill 不显示 tag，显示 keyword；Item 显示 tag 和 keyword（`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabProfile.cs:44`）。因此 tag mode 只在 tag section 可见时有 UI；keyword mode 在 Item 与 Skill 都可见。

`PackagesOnly` 是早期排他分支；测试要求它忽略 hero、tier、size、tag、keyword 和 day（`tests/CollectionFilterEngine.Tests/Program.cs:284`）。新增 mode 不应影响这个分支。

本地化现在由 `CollectionPanelText` 集中维护 CollectionPanel 文案，并通过 `L.Resolve(LocalizedTextSet)` 解析（`src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs:10`、`src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs:194`）。`LocalizedTextSet` 支持 English、简体、台湾繁体、香港繁体，以及德语/葡语/韩语/意大利语 fallback 构造（`src/BazaarPlusPlus.Localization/LocalizedTextSet.cs:6`），`L.Resolve` 从当前 language code 与中文地区模式解析文本（`src/BazaarPlusPlus.Localization/L.cs:17`）。

## 目标 UX

每个支持组内模式的 filter section header 变成横向布局：

```text
Tags                         [任一]
Weapon  Friend  Tool ...

Types                        [全部]
Damage  Shield  Haste ...
```

交互：

- 点击 header 右侧的小按钮，在 `Any` 和 `All` 之间切换。
- 按钮不应只显示图标；这里的逻辑对玩家不是常规工具图标，短文本更明确。
- 按钮 tooltip 解释当前模式和点击后的作用。
- 当该 facet 没有选中 chip 时，mode 不影响结果，但按钮仍可见；这样用户可以先决定逻辑再选 chip。
- section 隐藏时对应按钮也隐藏。比如 Skill 页不显示 tag section，所以 tag mode 按钮自然隐藏。

推荐文案：

| 概念 | English | 简中 | 繁中 TW/HK |
| --- | --- | --- | --- |
| Any 短标签 | Any | 任一 | 任一 |
| All 短标签 | All | 全部 | 全部 |
| Tag Any tooltip | Tags: match cards with any selected tag. Click to require all. | 标签：匹配任一已选标签的卡。点击切换为必须全部匹配。 | 標籤：匹配任一已選標籤的卡。點擊切換為必須全部匹配。 |
| Tag All tooltip | Tags: require every selected tag. Click to match any. | 标签：必须匹配所有已选标签。点击切换为任一匹配。 | 標籤：必須匹配所有已選標籤。點擊切換為任一匹配。 |
| Keyword Any tooltip | Types: match cards with any selected type. Click to require all. | 类型：匹配任一已选类型的卡。点击切换为必须全部匹配。 | 類型：匹配任一已選類型的卡。點擊切換為必須全部匹配。 |
| Keyword All tooltip | Types: require every selected type. Click to match any. | 类型：必须匹配所有已选类型。点击切换为任一匹配。 | 類型：必須匹配所有已選類型。點擊切換為任一匹配。 |

说明：

- 可见短标签使用 `Any/All` 而不是 `OR/AND`，因为这是玩家语义，不是布尔表达式语法。
- Tooltip 按 facet 分开，而不是写一个通用“当前组”，避免用户不知道“类型”按钮影响的是 keyword / `EHiddenTag`。
- 德语、葡语、韩语、意大利语第一版可使用 English fallback，因为当前 `CollectionPanelText` 多数新文案也只传 4 语言构造。若后续要补齐，可改用 6/8 参数构造，不影响 API。

## 数据模型

新增枚举：

```csharp
internal enum CollectionFacetMatchMode
{
    Any,
    All,
}
```

新增位置建议：`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs`。理由是这只是 CollectionPanel 的过滤状态，不是跨模块契约。

`CollectionFilterState` 新增：

```csharp
public CollectionFacetMatchMode TagMatchMode { get; set; } = CollectionFacetMatchMode.Any;
public CollectionFacetMatchMode KeywordMatchMode { get; set; } = CollectionFacetMatchMode.Any;
```

第一版不放进 `CollectionTabProfile`。Profile 决定 facet 是否可见/可用；mode 是用户状态，和 `Tags` / `Keywords` 的选中集合同层。

状态持久性：

- 当前 `_filter` 是 `CollectionPanel` 实例字段（`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:64`），打开面板时 `ApplyOpenSelection` 只更新英雄/来源/day 锚点，不重建整个 filter（`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:293`）。所以新增 mode 默认也会跨打开保留。
- 这和当前 day/package 等状态行为一致。若后续想跨游戏会话持久化，再单独引入 config；本轮不做。

## FilterEngine 改造

把当前两个 `Any*Match` 改成通用的 mode-aware helper：

```csharp
private static bool MatchesFacet<T>(
    IReadOnlyCollection<T> cardValues,
    HashSet<T> filterValues,
    CollectionFacetMatchMode mode
)
{
    return mode == CollectionFacetMatchMode.All
        ? AllSelectedValuesMatch(cardValues, filterValues)
        : AnySelectedValueMatches(cardValues, filterValues);
}
```

`All` 的方向必须是“所有已选 filter value 都在卡牌值里”，不是“卡牌所有值都在 filter 里”。即：

```csharp
foreach (var selected in filterValues)
{
    if (!cardValueSet.Contains(selected))
        return false;
}
return true;
```

调用点：

```csharp
if (
    tagFilterCount > 0
    && !MatchesFacet(card.Tags, filter.Tags, filter.TagMatchMode)
)
    continue;

if (
    keywordFilterCount > 0
    && !MatchesFacet(card.HiddenTags, filter.Keywords, filter.KeywordMatchMode)
)
    continue;
```

注意事项：

- `filterValues.Count == 0` 时不会调用 helper，因为现有 `tagFilterCount > 0` / `keywordFilterCount > 0` gate 已经挡住。
- `All` 模式下，如果卡牌没有 tags / hiddenTags，会正常失败。
- `PackagesOnly` 分支仍在这些 gate 之前；不要让 mode 参与 package-only 行为。
- `HashSet<T>` 没有稳定顺序，但匹配模式不依赖顺序。

## UI 改造

### ViewModel

`CollectionPanelViewModel` 新增：

```csharp
public CollectionFacetMatchMode TagMatchMode { get; set; } = CollectionFacetMatchMode.Any;
public CollectionFacetMatchMode KeywordMatchMode { get; set; } = CollectionFacetMatchMode.Any;
```

`CollectionPanel.RefreshView()` 填入 `_filter.TagMatchMode` 和 `_filter.KeywordMatchMode`。

### View callback

`CollectionPanelView` 构造函数新增两个 callback：

```csharp
Action toggleTagMatchMode,
Action toggleKeywordMatchMode
```

`CollectionPanel.EnsureView()` 中实现为：

```csharp
_filter.TagMatchMode = Toggle(_filter.TagMatchMode);
_scrollY = 0f;
ApplyFilters();
RefreshView();
```

keyword 同理。可以抽一个 private static `ToggleMatchMode`。

### Header 布局

当前 `CreateFilterSection` 只返回 label 与 chip row。建议新增重载，而不是改所有 section：

```csharp
private static VisualElement CreateFilterSection(
    VisualElement parent,
    string title,
    float marginTop,
    out VisualElement chipRow,
    out Label label,
    out VisualElement headerRow
)
```

默认 section 可以继续走旧重载。Tag / keyword 使用新重载，在 `headerRow` 里右侧加入 mode button。

样式：

- header row: `FlexDirection.Row`、`AlignItems.Center`。
- label: `flexGrow = 1`、`minWidth = 0`、NoWrap + Hidden 保持现有行为。
- mode button: 固定或最小宽度，建议 `Sizes.RunsTabWidth` 太宽，不适用；新增 `Sizes.FacetModeToggleWidth` 或局部常量，约 54-64 px。
- button 高度使用 `Sizes.ButtonStandardHeight` 或更紧凑的 `Sizes.InfoChipHeight`。因为它在 header 内，建议使用 `Sizes.InfoChipHeight`，视觉上接近 tag chips。
- 使用现有 `CreateButton` + `StyleButton` + `RefreshChip`，保持金色选中态/默认态一致。`All` 可以作为 selected/highlight，`Any` 作为默认态。

需要新增字段：

```csharp
private Button? _tagMatchModeButton;
private Button? _keywordMatchModeButton;
```

Refresh 时：

- `RefreshMatchModeButton(_tagMatchModeButton, model.TagMatchMode, CollectionPanelText.TagMatchModeTooltip(model.TagMatchMode));`
- `RefreshMatchModeButton(_keywordMatchModeButton, model.KeywordMatchMode, CollectionPanelText.KeywordMatchModeTooltip(model.KeywordMatchMode));`
- Section 隐藏时按钮随 parent 隐藏即可。

### 为什么不把按钮放进 chip row

放在 chip row 会让 mode button 与普通筛选 chip 混在一起，用户容易误认为它也是筛选值之一。header 右侧符合“这个按钮控制整组组合逻辑”的语义，也不会参与 `EnsureTagChips` / `EnsureKeywordChips` 的 row 清理。

## 国际化方案

所有新增字符串放在 `src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs`，继续使用 `LocalizedTextSet`。

新增 text set：

```csharp
private static readonly LocalizedTextSet FacetMatchAnyText = new(
    "Any",
    "任一",
    "任一",
    "任一"
);

private static readonly LocalizedTextSet FacetMatchAllText = new(
    "All",
    "全部",
    "全部",
    "全部"
);
```

新增 tooltip text set：

```csharp
private static readonly LocalizedTextSet TagMatchAnyTooltipText = new(...);
private static readonly LocalizedTextSet TagMatchAllTooltipText = new(...);
private static readonly LocalizedTextSet KeywordMatchAnyTooltipText = new(...);
private static readonly LocalizedTextSet KeywordMatchAllTooltipText = new(...);
```

新增 API：

```csharp
internal static string FacetMatchMode(CollectionFacetMatchMode mode) =>
    mode == CollectionFacetMatchMode.All ? Resolve(FacetMatchAllText) : Resolve(FacetMatchAnyText);

internal static string TagMatchModeTooltip(CollectionFacetMatchMode mode) =>
    mode == CollectionFacetMatchMode.All
        ? Resolve(TagMatchAllTooltipText)
        : Resolve(TagMatchAnyTooltipText);

internal static string KeywordMatchModeTooltip(CollectionFacetMatchMode mode) =>
    mode == CollectionFacetMatchMode.All
        ? Resolve(KeywordMatchAllTooltipText)
        : Resolve(KeywordMatchAnyTooltipText);
```

字体预热：

- `CollectionPanelView.EnsureCreated()` 当前会把一批 CollectionPanel 文案传给 `BppUiFont.RequestCharactersInTexture`（`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:208`）。新增 `CollectionPanelText.FacetMatchMode(...)` 与 tooltip 文案至少应把可见短标签加入预热字符串。
- Tooltip 字符是否必须预热可以按现有做法决定；如果担心首次 hover 缺字，可把四个 tooltip 都加入预热。新增文本不多，成本可控。

语言刷新：

- `RefreshChromeTexts()` 当前每次刷新会重设 header 文案和 package tooltip（`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:444`）。新增 mode button 的 text/tooltip 应在 `Refresh` 或 `RefreshChromeTexts` 中每次重设，避免语言/简繁模式切换后残留旧文案。
- 不要在按钮构造时只设置一次文本；tag/keyword chips 已经因为 locale 切换做了每次 `Refresh` 重设（`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:346`），mode button 应遵循同一原则。

中文简繁：

- 这里所有中文字符串都是 mode 敏感文本，使用 `LocalizedTextSet` 的四语言构造给出简体与繁体值。
- 不要手写调用 `ChineseScriptConverter`，除非是类似 `MatchCount` 这种动态插值串；本次新增都是固定文案，直接 `L.Resolve(LocalizedTextSet)`。

## 测试计划

### CollectionFilterEngine.Tests

新增/修改用例：

1. 默认 `Any` 保持现有行为：多 tag OR、多 item keyword OR、多 skill keyword OR 的现有断言继续通过。
2. `TagMatchMode = All`：
   - 构造 `Weapon`、`Potion`、`Weapon+Potion` 三张 item。
   - 选中 `Weapon` 和 `Potion`。
   - 期望只返回 `Weapon+Potion`。
3. `KeywordMatchMode = All` on Item：
   - 构造 `Damage`、`Shield`、`Damage+Shield` 三张 item。
   - 选中 `Damage` 和 `Shield`。
   - 期望只返回 `Damage+Shield`。
4. `KeywordMatchMode = All` on Skill：
   - 同样构造 skill，确认 Skill tab 走同一语义。
5. `Damage + DamageReference` 的 All：
   - 构造仅 `Damage`、仅 `DamageReference`、同时含两者的 item。
   - 选中两个 keyword，`All` 只返回同时含两者的卡。
   - 这是为了锁定 reference subsection 不具备特殊 OR 语义。
6. PackagesOnly 不受 mode 影响：
   - `PackagesOnly=true`，设置 `TagMatchMode=All`、`KeywordMatchMode=All`，并选一些 facet。
   - 期望仍返回所有 package，延续当前 package-only 测试语义。

### UI/文本测试

当前没有 CollectionPanel UITK 结构单测。第一版可不新增 UI 单测，但至少做以下静态/运行验证：

- `./run.sh test`
- `./run.sh build`
- 手动打开 CollectionPanel：
  - Item 页 tag header 显示 mode 按钮。
  - Item 页 keyword header 显示 mode 按钮。
  - Skill 页不显示 tag section，keyword header 显示 mode 按钮。
  - 点击 mode 后结果数量变化符合 Any/All。
  - 切换语言/中文模式后按钮短标签和 tooltip 重新解析。

如果后续要补 UI 自动化，优先加轻量的 view-model/text 级测试，而不是搭新的 Unity UITK runner。

## 分阶段实施

1. Engine + tests first
   - 新增 `CollectionFacetMatchMode`。
   - 给 `CollectionFilterState` 加默认 `Any` mode。
   - 改 `CollectionFilterEngine` helper。
   - 补 `CollectionFilterEngine.Tests`。
   - 跑 focused runner：`dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`。

2. Text / i18n
   - 在 `CollectionPanelText` 添加短标签和 tooltip 文案。
   - 把新增可见短标签加入 `BppUiFont.RequestCharactersInTexture` 的预热字符串。
   - 确认 `RefreshChromeTexts` 或 `Refresh` 会每次刷新 mode button 文案。

3. ViewModel + callback
   - `CollectionPanelViewModel` 增加两个 mode 字段。
   - `CollectionPanel.RefreshView()` 传入 `_filter` mode。
   - `CollectionPanelView` 构造函数增加两个 callback。
   - `CollectionPanel.EnsureView()` 中实现 toggle + `ApplyFilters()` + `RefreshView()`。

4. Header UI
   - 增加 header-row 版 `CreateFilterSection` 或小 helper。
   - Tag / keyword section 使用 header-row helper。
   - 添加 `_tagMatchModeButton` / `_keywordMatchModeButton`。
   - `Refresh` 中更新按钮 text、tooltip、highlight。

5. Full verification
   - `./run.sh test`
   - `./run.sh build`
   - In-game manual smoke：Item/Skill、Any/All、中文简繁切换、package-only 回归。

## 风险与确认点

- 可见短标签用 `Any/All` 还是 `OR/AND`：本计划推荐 `Any/All / 任一/全部`，因为它是玩家语义；如果团队偏工程术语，可替换为 `OR/AND`，但 tooltip 仍应本地化解释。
- `KeywordMatchMode` 是否 Item/Skill 共用：本计划按当前 `Keywords` 单集合设计共用。若想 Item 和 Skill 各自保留不同 keyword mode，需要引入 per-tab state，本轮不建议扩大。
- mode 是否要持久化到配置：本计划不持久化到磁盘，只跟随当前面板 `_filter` 生命周期。若用户强烈依赖该偏好，再另开设置项。
- `All` 模式下结果可能非常少，尤其 `Damage + DamageReference` 这类 reference 组合。这个行为是预期结果，tooltip 要讲清楚“必须全部匹配”。
- header 横向空间有限。短标签必须保持短，tooltip 承担解释；不要把“任一/全部匹配”完整句子放进按钮。
