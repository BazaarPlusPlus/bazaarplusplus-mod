> **Status: IMPLEMENTED (historical).** Shipped 2026-05-31. Current state: [CollectionPanelView.Tree.cs](../../../Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs), [HistoryPanelUiToolkitView.Tree.cs](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs), and [Sizes.cs](../../../Infrastructure/UiTokens/Sizes.cs).

# History / Collection Panel 右侧操作区布局设计规格

Status: Implemented 2026-05-31. Historical design note; current state is the code linked above.

> 范围：统一 HistoryPanel 和 CollectionPanel 的大布局，把主要浏览/检视内容放在左侧大区域，把标题、状态、筛选、动作按钮收敛到右侧固定比例操作区。本文只描述布局方案和代码落点，不直接实现。
> 关联：[2026-05-31-collection-panel-design.md](../2026-05-31-collection-panel-design.md)、[2026-05-29-historypanel-fullscreen-responsive-design.md](2026-05-29-historypanel-fullscreen-responsive-design.md)、[history-panel.md](../../features/history-panel.md)。

## 1. 结论

操作区应该放在右边，且 HistoryPanel 和 CollectionPanel 应统一成同一个布局模型：

```text
panel(row)
├── coreArea(flexGrow, minWidth 0)
│   └── 真正的浏览/检视主体
└── operationRail(24%, min 300, max 560)
    └── 标题 / 状态 / 筛选 / 动作
```

理由：

- 这两个面板都是游戏内浏览/检视工具，不是传统设置页。第一视觉应给核心内容：Collection 的卡牌 grid、History 的 Runs/Battles/Preview。
- 操作区是辅助控制，不应抢第一列。右侧 rail 更接近 inspector/control rail：筛选、模式、状态、Replay/Delete/Close 都是对左侧主体的控制。
- HistoryPanel 的 `Runs -> Battles -> Preview` 是一个连续选择链，不应该把 Runs 拆进信息区。Runs 和 Battles 应保持同级横排。
- CollectionPanel 当前左操作区可用，但它让用户第一眼先看到筛选项而不是图鉴本体；右侧 rail 更符合“图鉴展示工具”的意图。

最终方案：

- CollectionPanel：左侧全高卡牌 grid，右侧 24% 操作 rail。
- HistoryPanel：左侧 core 里保留 `Runs + Battles` 同级横排，并在其下方放 preview；右侧 24% 操作 rail。

## 2. 当前代码位置

### CollectionPanel

- UITK 外壳：`Game/CollectionPanel/Ui/CollectionPanelView.cs`
- 当前布局树：`Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs`
- 当前左列比例：`BuildOperationColumn`
  - `flexBasis = Length.Percent(24f)`
  - `minWidth = 300f`
  - `maxWidth = 560f`
- 当前右侧 grid：`BuildGrid`
- grid 视口几何桥接：`CollectionPanelView.OnGridViewportGeometryChanged`
- grid overlay / virtualizer：`Game/CollectionPanel/Grid/CollectionGridOverlay.cs`、`CollectionGridVirtualizer.cs`

### HistoryPanel

- UITK 外壳：`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs`
- 当前布局树：`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs`
- 当前旧结构：
  - `BuildTree`: header -> content -> footer
  - `BuildContent`: `Runs` 30% + `Battles` 横排，preview 在 content 底部
  - `BuildFooter`: Replay/Delete/Close 等动作
- 当前比例 token：`Infrastructure/UiTokens/Sizes.cs`
  - `RunsColumnWidthPercent = 30f`
  - `PreviewHeightPercent = 28f`
  - `FooterHeight = 56f`
- preview 几何桥接：`HistoryPanelUiToolkitView.OnPreviewContainerGeometryChanged`
- native preview 缩放：`HistoryPanel.ApplyPreviewContainerBounds`

## 3. 共享布局契约

把 CollectionPanel 当前 hardcode 的左列比例抽成共享 token，两个面板都使用同一个 rail 契约。

建议新增到 `Infrastructure/UiTokens/Sizes.cs`：

```csharp
public const float OperationRailWidthPercent = 24f;
public const float OperationRailMinWidth = 300f;
public const float OperationRailMaxWidth = 560f;

// History core 内部：selector row 占 core 高度的一部分，preview 吃剩余空间。
public const float HistorySelectorRowHeightPercent = 36f;
public const float HistorySelectorRowMinHeight = 240f;
```

命名用 `OperationRail`，不要叫 `RunsColumn` 或 `SideInfoColumn`，因为它会被 Collection 和 History 共用，语义是“辅助控制 rail”。

## 4. CollectionPanel 目标布局

### 4.1 逻辑

CollectionPanel 改为：

```text
panel(row)
├── gridViewport(flexGrow)
└── operationRail(24%, min 300, max 560)
    ├── title + close
    ├── subtitle
    ├── count chip
    ├── Item / Skill tabs
    ├── search
    ├── clear / package / merchant placeholder
    ├── hero chips
    ├── tier chips
    ├── size chips
    └── status
```

核心变化：

- `BuildGrid(panel)` 先加，成为左侧主体。
- `BuildOperationColumn(panel)` 后加，改名为 `BuildOperationRail(panel)` 更准确。
- 间距从 grid 的 `marginLeft` 改为 operation rail 的 `marginLeft`。
- grid 的视觉、滚动、overlay bounds、virtualizer 不变。
- 右侧操作 rail 仍用同一批 UI 元素和 Refresh 逻辑；只是位置从左变右。

### 4.2 对应代码

`Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs`：

```csharp
private void BuildTree(VisualElement root)
{
    var panel = new VisualElement();
    panel.style.flexGrow = 1f;
    panel.style.backgroundColor = Colors.HistoryPanelBackground;
    panel.style.paddingLeft = UiSpacing.PanelPadding;
    panel.style.paddingRight = UiSpacing.PanelPadding;
    panel.style.paddingTop = UiSpacing.PanelPadding;
    panel.style.paddingBottom = UiSpacing.Xxl;
    panel.style.flexDirection = FlexDirection.Row;
    root.Add(panel);

    BuildGrid(panel);
    BuildOperationRail(panel);
}

private void BuildOperationRail(VisualElement parent)
{
    var rail = new VisualElement();
    rail.style.flexDirection = FlexDirection.Column;
    rail.style.flexGrow = 0f;
    rail.style.flexShrink = 0f;
    rail.style.flexBasis = Length.Percent(Sizes.OperationRailWidthPercent);
    rail.style.minWidth = Sizes.OperationRailMinWidth;
    rail.style.maxWidth = Sizes.OperationRailMaxWidth;
    rail.style.marginLeft = UiSpacing.ColumnGap;
    parent.Add(rail);

    // Move the current BuildOperationColumn body here unchanged, replacing `column` with `rail`.
}
```

`BuildGrid` removes the old left gap:

```csharp
private void BuildGrid(VisualElement parent)
{
    _gridViewport = new VisualElement();
    _gridViewport.style.flexGrow = 1f;
    _gridViewport.style.flexShrink = 1f;
    _gridViewport.style.minHeight = 0f;
    _gridViewport.style.minWidth = 0f;
    _gridViewport.style.backgroundColor = Colors.CollectionGridCaseBackground;
    UiStyle.Radius(_gridViewport.style, Radii.Md);
    UiStyle.Border(_gridViewport.style, Borders.Thin, Colors.HistoryListFrameBorder);
    _gridViewport.style.overflow = Overflow.Hidden;
    parent.Add(_gridViewport);

    // Existing ScrollView / spacer / empty label code remains unchanged.
}
```

## 5. HistoryPanel 目标布局

### 5.1 逻辑

HistoryPanel 改为：

```text
panel(row)
├── coreArea(flexGrow)
│   ├── selectorRow(row)
│   │   ├── runsSection(30% of core, hidden in Ghost mode)
│   │   └── battlesSection(flexGrow)
│   └── previewContainer(flexGrow)
└── operationRail(24%, min 300, max 560)
    ├── title
    ├── subtitle
    ├── count / battle / database chips
    ├── status
    ├── Runs / Ghost tabs
    ├── refresh final builds
    ├── ghost filters
    ├── selected battle summary
    └── replay / record+replay / delete / close
```

关键决策：

- `Runs` 和 `Battles` 继续同级横排。它们不是操作区；它们是 HistoryPanel 的主体浏览链路。
- `Preview` 继续在 `Runs/Battles` 下方，但它现在位于左侧 core 内，并获得比旧 `PreviewHeightPercent = 28f` 更大的弹性空间。
- 右侧 rail 承载全局信息、模式控制、状态和动作按钮。
- Ghost 模式下隐藏 `runsSection`，`battlesSection` 自动占满 selector row 宽度；这与当前 `Refresh` 的语义一致，只是 margin 逻辑从横排同级 sections 内部移动到 core 内部。

### 5.2 对应代码

`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs` 的 `BuildTree` 改为：

```csharp
private void BuildTree(VisualElement root)
{
    var panel = new VisualElement();
    panel.style.flexGrow = 1f;
    panel.style.backgroundColor = Colors.HistoryPanelBackground;
    panel.style.paddingLeft = UiSpacing.PanelPadding;
    panel.style.paddingRight = UiSpacing.PanelPadding;
    panel.style.paddingTop = UiSpacing.PanelPadding;
    panel.style.paddingBottom = UiSpacing.Xxl;
    panel.style.flexDirection = FlexDirection.Row;
    root.Add(panel);

    BuildCoreArea(panel);
    BuildOperationRail(panel);
}
```

新增 `BuildCoreArea`：

```csharp
private void BuildCoreArea(VisualElement parent)
{
    var core = new VisualElement();
    core.style.flexGrow = 1f;
    core.style.flexShrink = 1f;
    core.style.minWidth = 0f;
    core.style.minHeight = 0f;
    core.style.flexDirection = FlexDirection.Column;
    parent.Add(core);

    var selectorRow = new VisualElement();
    selectorRow.style.flexDirection = FlexDirection.Row;
    selectorRow.style.flexGrow = 0f;
    selectorRow.style.flexShrink = 0f;
    selectorRow.style.height = Length.Percent(Sizes.HistorySelectorRowHeightPercent);
    selectorRow.style.minHeight = Sizes.HistorySelectorRowMinHeight;
    selectorRow.style.minWidth = 0f;
    core.Add(selectorRow);

    BuildRunsSection(selectorRow);
    BuildBattlesSection(selectorRow);
    BuildPreview(core);
}
```

把旧 `BuildContent` 拆成三个局部构建函数。

`BuildRunsSection`：

```csharp
private void BuildRunsSection(VisualElement parent)
{
    _runsSection = CreateSectionPanel(null);
    _runsSection.style.width = Length.Percent(Sizes.RunsColumnWidthPercent);
    _runsSection.style.flexGrow = 0f;
    _runsSection.style.flexShrink = 0f;
    _runsSection.style.minHeight = 0f;
    parent.Add(_runsSection);

    _runsSection.Add(CreateSectionTitle(HistoryPanelText.RunsTab()));
    _runsList = CreateRunList();
    _runsSection.Add(CreateListFrame(_runsList));
}
```

`BuildBattlesSection`：

```csharp
private void BuildBattlesSection(VisualElement parent)
{
    _battlesSection = CreateSectionPanel(null);
    _battlesSection.style.flexGrow = 1f;
    _battlesSection.style.flexShrink = 1f;
    _battlesSection.style.minHeight = 0f;
    _battlesSection.style.minWidth = 0f;
    _battlesSection.style.marginLeft = UiSpacing.ColumnGap;
    parent.Add(_battlesSection);

    _battlesTitle = CreateSectionTitle(HistoryPanelText.Battles());
    _battlesTitle.style.marginTop = UiSpacing.None;
    _battlesSection.Add(_battlesTitle);

    _runsBattleSubtitle = CreateLabel(
        Sizes.FontSmall,
        FontStyle.Normal,
        Colors.HistoryFooterSecondaryText
    );
    _runsBattleSubtitle.style.marginTop = UiSpacing.Xs;
    _runsBattleSubtitle.style.display = DisplayStyle.None;
    _battlesSection.Add(_runsBattleSubtitle);

    _battleList = CreateBattleList();
    _battleList.style.marginTop = UiSpacing.Md;
    _battlesSection.Add(CreateListFrame(_battleList));
}
```

`BuildPreview`：

```csharp
private void BuildPreview(VisualElement parent)
{
    _ghostOpponentEliminatedNotice = CreateLabel(
        Sizes.FontBody,
        FontStyle.Bold,
        Colors.HistoryEliminatedText
    );
    UiStyle.FixedHeight(_ghostOpponentEliminatedNotice.style, Sizes.EliminatedNoticeHeight);
    _ghostOpponentEliminatedNotice.style.marginTop = UiSpacing.Lg;
    UiStyle.HorizontalPadding(_ghostOpponentEliminatedNotice.style, UiSpacing.Xxl);
    _ghostOpponentEliminatedNotice.style.unityTextAlign = TextAnchor.MiddleCenter;
    _ghostOpponentEliminatedNotice.style.whiteSpace = WhiteSpace.NoWrap;
    _ghostOpponentEliminatedNotice.style.backgroundColor = Colors.HistoryEliminatedBackground;
    UiStyle.Radius(_ghostOpponentEliminatedNotice.style, Radii.Row);
    UiStyle.Border(
        _ghostOpponentEliminatedNotice.style,
        Borders.Thin,
        Colors.HistoryEliminatedNoticeBorder
    );
    _ghostOpponentEliminatedNotice.style.display = DisplayStyle.None;
    parent.Add(_ghostOpponentEliminatedNotice);

    _previewContainer = new VisualElement();
    _previewContainer.style.flexGrow = 1f;
    _previewContainer.style.flexShrink = 1f;
    _previewContainer.style.minHeight = 0f;
    _previewContainer.style.backgroundColor = Colors.HistoryPreviewBackground;
    UiStyle.Radius(_previewContainer.style, Radii.Md);
    UiStyle.Border(_previewContainer.style, Borders.Thin, Colors.HistoryListFrameBorder);
    _previewContainer.style.position = Position.Relative;
    _previewContainer.style.overflow = Overflow.Hidden;
    _previewContainer.style.marginTop = UiSpacing.Lg;
    parent.Add(_previewContainer);

    // Existing _previewImage / _previewStatusLabel / _previewDebugLabel setup stays the same.
}
```

新增 `BuildOperationRail`，吸收旧 `BuildHeader` 和 `BuildFooter`：

```csharp
private void BuildOperationRail(VisualElement parent)
{
    var rail = new VisualElement();
    rail.style.flexDirection = FlexDirection.Column;
    rail.style.flexGrow = 0f;
    rail.style.flexShrink = 0f;
    rail.style.flexBasis = Length.Percent(Sizes.OperationRailWidthPercent);
    rail.style.minWidth = Sizes.OperationRailMinWidth;
    rail.style.maxWidth = Sizes.OperationRailMaxWidth;
    rail.style.minHeight = 0f;
    rail.style.marginLeft = UiSpacing.ColumnGap;
    parent.Add(rail);

    _title = CreateLabel(Sizes.FontTitle, FontStyle.Bold, Colors.HistoryTitleText);
    rail.Add(_title);

    _subtitle = CreateLabel(Sizes.FontBody, FontStyle.Normal, Colors.HistorySubtitleText);
    _subtitle.style.whiteSpace = WhiteSpace.Normal;
    _subtitle.style.marginTop = UiSpacing.Md;
    rail.Add(_subtitle);

    // Move count/battle/database chips here from old BuildHeader.
    // Move _statusLabel here from old chip row.
    // Move _runsTabButton / _ghostTabButton / _finalBuildRefreshButton here.
    // Move _ghostFilterRow here.
    // Move footer summary labels and action buttons here from old BuildFooter.
}
```

右侧 rail 的动作区建议靠底：

```csharp
rail.Add(CreateSpacer());

_footerPrimary = CreateLabel(Sizes.FontFooterPrimary, FontStyle.Bold, Colors.White);
_footerPrimary.style.whiteSpace = WhiteSpace.Normal;
rail.Add(_footerPrimary);

_footerSecondary = CreateLabel(
    Sizes.FontSmall,
    FontStyle.Normal,
    Colors.HistoryFooterSecondaryText
);
_footerSecondary.style.whiteSpace = WhiteSpace.Normal;
_footerSecondary.style.marginTop = UiSpacing.Md;
rail.Add(_footerSecondary);

var actions = new VisualElement();
actions.style.flexDirection = FlexDirection.Column;
actions.style.marginTop = UiSpacing.Lg;
rail.Add(actions);

// Buttons should use full rail width rather than the old horizontal footer widths.
```

建议为 rail 内按钮新增宽度策略，不复用旧 footer 横排宽度：

```csharp
private static Button CreateRailButton(string text, Action onClick)
{
    var button = new Button(() => onClick()) { text = text };
    button.style.height = Sizes.ButtonFooterHeight;
    button.style.width = Length.Percent(100f);
    button.style.flexGrow = 0f;
    button.style.flexShrink = 0f;
    button.style.unityFont = GetUiFont();
    button.style.unityTextAlign = TextAnchor.MiddleCenter;
    button.style.justifyContent = Justify.Center;
    button.style.alignItems = Align.Center;
    UiStyle.Padding(button.style, UiSpacing.None);
    button.style.backgroundColor = Colors.HistoryButtonBackground;
    button.style.color = Colors.White;
    UiStyle.Border(button.style, Borders.Thin, Colors.HistoryButtonBorder);
    UiStyle.Radius(button.style, Radii.Md);
    return button;
}
```

## 6. Refresh 逻辑修改

### CollectionPanel

`Refresh(CollectionPanelViewModel model)` 基本不需要语义变化。只要字段还是 `_itemTabButton`、`_gridViewport`、`_gridScrollView` 等，现有刷新逻辑可保留。

需要确认：

- `_gridViewport` 的 `GeometryChangedEvent` 仍然触发左侧大 grid bounds。
- `GridViewportBoundsChanged` 仍然用物理像素发给 overlay。
- `ReadScrollYPixels()` 与 spacer 高度换算不变。

### HistoryPanel

`HistoryPanelUiToolkitView.Refresh` 需要调整布局相关逻辑：

```csharp
_runsSection!.style.display =
    model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.None : DisplayStyle.Flex;

_battlesSection!.style.marginLeft =
    model.SectionMode == HistorySectionMode.Ghost ? UiSpacing.None : UiSpacing.ColumnGap;

_battlesTitle!.style.display =
    model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.None : DisplayStyle.Flex;

_ghostFilterRow!.style.display =
    model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.Flex : DisplayStyle.None;
```

以上语义可以保留。变化只是 `_ghostFilterRow` 不再属于 `_battlesSection`，而是在右侧 rail；`_battlesSection.marginLeft` 仍然负责 Ghost 模式下移除 Runs 隐藏后的空 gap。

如果 `Runs` 隐藏后 selector row 里 battles 没有自动铺满，需要同时加：

```csharp
_battlesSection!.style.flexGrow = 1f;
_battlesSection.style.flexShrink = 1f;
```

preview 相关的 `SetPreviewStatus`、`SetPreviewDebug`、`SetPreviewTexture` 和 `PreviewContainerBoundsChanged` 不需要改。

## 7. 需要修改的文件清单

### 必改

| 文件 | 修改 |
|---|---|
| `Infrastructure/UiTokens/Sizes.cs` | 新增 `OperationRailWidthPercent` / `OperationRailMinWidth` / `OperationRailMaxWidth` / History selector row token。 |
| `Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs` | `BuildTree` 顺序改为 grid -> rail；`BuildOperationColumn` 改为 `BuildOperationRail`；rail 使用共享 token；grid 移除 `marginLeft`。 |
| `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs` | `BuildTree` 改为 core -> rail；拆分 `BuildCoreArea` / `BuildRunsSection` / `BuildBattlesSection` / `BuildPreview` / `BuildOperationRail`；废弃旧顶层 `BuildFooter`。 |
| `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs` | 检查并微调 `Refresh` 的 section display / margin / ghost filter 逻辑。 |

### 可能需要

| 文件 | 修改 |
|---|---|
| `Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Style.cs` | 新增 `CreateRailButton` 或扩展 `CreateButton` 支持 `Length.Percent(100f)` 宽度。 |
| `Infrastructure/UiTokens/Sizes.cs` | 如 rail 内横向按钮不再使用旧 footer 宽度，可保留旧常量给别处用，不急着删除。 |
| `docs/design/2026-05-31-collection-panel-design.md` | 实现完成后补 as-built note，说明 CollectionPanel 从左操作列改为右操作 rail。 |
| `docs/features/history-panel.md` | 实现并稳定后同步 living feature doc；设计阶段不改。 |

## 8. 不做的事

- 不重写 History 数据模型、SQLite、Ghost sync、Replay service。
- 不改 Collection grid virtualizer、overlay sorting order、hover 路径。
- 不把 Runs 放进操作区。Runs/Battles/Preview 都属于 History 的 core。
- 不引入 USS 或 AssetBundle stylesheet。现有 UI 仍用 inline style + shared tokens。
- 不在这一版做响应式左右互换或用户可配置 rail 位置。

## 9. 风险与验证

### 风险

- History rail 内按钮从横排变纵排后，中文长文案可能换行或挤压。需要优先检查 `Record and Replay` / `Download Replay` / `Replay unavailable` 类文案。
- `ListView` 在 selector row 高度变小后，run/battle 行密度可能偏紧；如果实际视觉不舒服，调 `HistorySelectorRowHeightPercent`，不要改 row 内部结构。
- CollectionPanel grid 左移后 overlay bounds 会变化，但桥接逻辑基于 `_gridViewport.worldBound`，理论上无需改；仍需游戏内验证 hover/scroll/click。
- History preview 变大后 `BattleBoardPreview` 会自动按 container bounds 缩放，可能暴露更明显的空白/居中问题；这是 layout 放大后的视觉调参，不是数据问题。

### 验证

最小自动验证：

```bash
dotnet build BazaarPlusPlus.csproj --no-restore
dotnet run --project tests/CollectionGridLayout.Tests/CollectionGridLayout.Tests.csproj
dotnet run --project tests/HistoryPanelPreview.Tests/HistoryPanelPreview.Tests.csproj
```

游戏内验证：

- CollectionPanel：打开图鉴，grid 应在左侧大区域；右侧筛选 rail 可点击；搜索、tab、chip、滚轮、hover tooltip 都正常。
- CollectionPanel：点击卡牌后，非卡区域点击和滚轮仍能回到 UITK，不得重新出现透明 blocker 类问题。
- HistoryPanel Runs 模式：Runs 和 Battles 同级横排；选择 run 后 battle 列表刷新；选择 battle 后 preview 更新；右侧 Replay/Delete/Close 可用。
- HistoryPanel Ghost 模式：Runs 隐藏，Battles 铺满 selector row；Ghost filter 在右侧 rail 可用；preview 和 eliminated notice 正常。
- 1440p / 4K：Collection grid overlay 与 UITK bounds 对齐；History preview overlay 与 `_previewContainer` 对齐。

## 10. 推荐实施顺序

1. 抽共享 token，但只替换 Collection 的 hardcode，不改变行为。
2. 改 CollectionPanel 为 core-left / rail-right，跑 build，进游戏验证 grid bounds、hover、scroll。
3. 改 HistoryPanel tree 拆分，但先保持旧视觉比例接近：selector row 36%，preview flex。
4. 把 History footer/header 内容迁到右 rail，保留原 refresh/model 字段。
5. 游戏内验证两个面板；按实际视觉只调 token，不改结构。
