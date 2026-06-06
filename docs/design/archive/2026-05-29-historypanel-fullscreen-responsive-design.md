> **Status: IMPLEMENTED (historical).** Shipped 2026-05-29. Current state: [history-panel.md](../../features/history-panel.md).

# History Panel 全屏化 + 相对布局

> ✅ **已实现（2026-05-29）。** 落地与本文有两处偏差：(1) `match` 采用 `1f`（纯按高），(2) 应用户决定，
> F9 调参器与预览调试色块**已删除**（本文「非目标」原写保留）。当前状态见
> [history-panel.md](../../features/history-panel.md)（Known Issues 节）。

> 设计文档。把 HistoryPanel 从「屏幕居中的固定 1280×1020 小方块」改成「边到边全屏、按相对比例
> 自适应」的面板，使其在 16:9 / 21:9 超宽 / 4K（以及窗口拖动改尺寸）下行为一致：永不溢出、
> 竖向密度恒定、预览棋盘正确对位。这是一次**响应式布局改造**，不改视觉风格、不动预览渲染管线。

## Scope

只动 HistoryPanel 的**外层布局与缩放口径**：

- `PanelSettings` 的缩放匹配方式（按宽 → 按高）。
- 面板外层从「固定尺寸盒子 + 居中」改成「flex 填满 + 百分比」。
- 去掉背景压暗遮罩。
- 预览叠加层的缩放口径与新匹配方式对齐。

### 非目标

- **不改视觉风格 / 控件 / 配色 / 字号 token 的设计值**。
- **不重排成三栏**（Runs｜Battles｜Preview 并排的 C 方案已排除）。
- **不处理「大屏内容空旷」**：用户已确认接受边到边拉伸带来的留白。
- **不动预览渲染管线**：战斗板预览维持当前的独立 `ScreenSpaceOverlay` Canvas 方案
  （[BattleBoardPreview.cs](../../../Game/HistoryPanel/Preview/BattleBoardPreview.cs)）。
  注：曾有一版「预览改回 RenderTexture 贴进 UI Toolkit」的设计，因 URP 下离屏 Camera→RT 无法渲染
  uGUI 而废弃、已回退到 overlay 方案（决策记录见
  [history-panel.md](../../features/history-panel.md)（预览渲染节））。本次在 overlay 方案之上做布局适配。
- **不删除临时 F9 调参器 / 调试色块**（[HistoryPanel.PreviewTunerDebug.cs](../../../Game/HistoryPanel/HistoryPanel.PreviewTunerDebug.cs)），
  仅同步它依赖的缩放口径。

## Background

### 当前实现 = 一个被整体缩放的固定长宽比盒子

- [HistoryPanelUiToolkitView.cs:91-96](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs#L91-L96)：
  `PanelSettings.scaleMode = ScaleWithScreenSize`，`referenceResolution = 1920×1080`，`sortingOrder = 26`。
  **未设 `match`**，取默认值 0 → **按宽度匹配**（缩放系数 = `Screen.width / 1920`）。
- [HistoryPanelUiToolkitView.Tree.cs:13-34](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs#L13-L34)：
  全屏 `overlay`（半透明 `Colors.HistoryOverlay` 压暗 + `justifyContent/alignItems: Center`）里居中一个
  **固定 1280×1020**、圆角 `Radii.Panel` 的不透明 `panel`。
- 固定结构尺寸（[Sizes.cs:7-12](../../../Infrastructure/UiTokens/Sizes.cs#L7-L12)）：
  `HistoryPanelWidth=1280`、`HistoryPanelHeight=1020`、`HistoryContentHeight=792`、
  `RunsColumnWidth=500`、`PreviewHeight=284`、`FooterHeight=56`。
- 内部其实已大量用 flex：`content` 行内 `columns` flex-grow、battles `flex-grow:1`、列表 `height:100%`。
  问题只在**最外层是固定长宽比盒子**。

### 表现：跨分辨率不一致

- 16:9（1080p）：缩放 1.0，正常。
- 4K（16:9）：缩放 2.0，同比例放大，正常。
- **21:9 超宽**（如 3440×1440）：缩放 = 3440/1920 ≈ 1.79，面板渲染高度 ≈ 1020×1.79 ≈ 1826px
  **> 屏幕 1440px → 上下溢出被裁**；同时宽度只占约 2293/3440 ≈ 67%，**两侧留白浪费**。

### 预览叠加层已经是「相对」的（好消息）

- [BattleBoardPreview.cs:262-265](../../../Game/HistoryPanel/Preview/BattleBoardPreview.cs#L262-L265)：独立
  `ScreenSpaceOverlay` Canvas（`sortingOrder = 27`），渲染真实卡片 prefab。
- [HistoryPanel.cs:362-385](../../../Game/HistoryPanel/HistoryPanel.cs#L362-L385) `ApplyPreviewContainerBounds`：
  把 UI Toolkit 预览容器的 `worldBound`（真实屏幕像素）翻译成 clip 位置/尺寸，并按
  `autoFitScale = min(clipW/2400, clipH/600)` 把原生 2400×600 棋盘自适应进去。
- 因此**容器一旦改成响应式，叠加层会自动跟随**——它本就读容器的真实屏幕矩形。唯一要对齐的是
  [HistoryPanel.cs:390-393](../../../Game/HistoryPanel/HistoryPanel.cs#L390-L393) `ComputePreviewPanelScale()`
  里写死的 `Screen.width / 1920`（假设了按宽缩放）。

### 开关 / 输入行为（全屏化需保持不回归）

- [HistoryPanelAccessPolicy.cs](../../../Game/HistoryPanel/HistoryPanelAccessPolicy.cs)：`CanOpen = !isInCombat`。
- [HistoryPanel.cs:160-188](../../../Game/HistoryPanel/HistoryPanel.cs#L160-L188) `Update()`：开战自动关闭；
  **Esc** 关闭；热键切换。**无「点击空白处关闭」逻辑**——全屏化不削弱任何关闭手段。

## Goal / 验收标准

1. 面板在 16:9 / 21:9 / 4K 及窗口拖动改尺寸时都**铺满屏幕、不溢出、不裁切**。
2. **竖向密度一致**：行高 / 字号 / 间距相对屏幕高度缩放，各分辨率下每屏可见行数比例相同。
3. 预览棋盘在各分辨率下**正确居中、对位、不变形**（容器变化时叠加层自动跟随 auto-fit）。
4. **背景压暗遮罩移除**；面板不透明、**直角**、铺满全屏。
5. 关闭手段（Close / Esc / 热键）、战斗自动关闭等行为**无回归**。
6. 若触及预览相关代码，`HistoryPanelPreview.Tests`（signature gate / generation guard）保持绿。

## Approach（三个杠杆 + 去遮罩）

### 关于「相对像素」的口径说明

在 `ScaleWithScreenSize` 下，代码里的 `px`（字号 14、行高 98…）**本就是相对参考分辨率的**、运行时整体缩放。
所以这次**不是把 px 改成百分比**，而是修正两件真正导致「2D / 不一致」的事：固定长宽比的外层盒子、按宽匹配。
设计 token（[Sizes.cs](../../../Infrastructure/UiTokens/Sizes.cs) 的字号/间距/行高）**保持不变**。

### 杠杆 1 · 缩放口径：按宽 → 按高

[HistoryPanelUiToolkitView.cs:91-96](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs#L91-L96)
增设 `PanelSettings.match = 1f`（按高度匹配），其余维持。效果：token 按**屏幕高度**缩放，竖向密度恒定、
超宽屏不再因按宽放大而竖向溢出。

> ⚠️ 实现时按游戏的 Unity 版本反射核对 `PanelSettings.match` / `screenMatchMode` 的确切字段名与语义。

### 杠杆 2 · 外层布局：flex 填满 + 百分比

[HistoryPanelUiToolkitView.Tree.cs](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs)：

- **去掉 `overlay` 压暗层**：`panel` 直接作为 `root` 的子元素铺满（`width/height: 100%` 或沿用 root 的
  absolute inset:0）。不再需要居中容器；面板不透明、自身即可拦截全屏输入。
- **panel**：移除固定 `1280×1020`；铺满屏幕；保留内边距 `PanelPadding`；**圆角 `Radii.Panel` → 直角（0）**；
  **背景必须完全不透明**——去掉遮罩后 panel 是唯一遮住背后游戏的层，若 `Colors.HistoryPanelBackground`
  带 alpha 需调为不透明。
- **content**：移除固定 `792` 及 `min/max`，改 `flex-grow:1` 吃掉 header 与 footer 间的空间。
- **runs 栏**：移除固定 `500` 及 `min/max`，改 `width: Length.Percent(30)`（纯比例，无上限）；
  battles 维持 `flex-grow:1`。
- **preview 容器**：移除固定 `284` 及 `min/max`，改取 content 高度的约 `Length.Percent(28)`
  （棋盘 auto-fit 自动跟随新矩形）。
- **header / footer**：维持现状——header 内容自适应高度，footer 保留固定高度 token `Sizes.FooterHeight`；
  两者的 px 值都会随按高匹配自动缩放，无需改动。

[Sizes.cs](../../../Infrastructure/UiTokens/Sizes.cs)：移除/改造结构性常量
`HistoryPanelWidth / HistoryPanelHeight / HistoryContentHeight / RunsColumnWidth / PreviewHeight`；
按需引入比例常量（如 `RunsColumnWidthPercent = 30`、`PreviewHeightPercent = 28`）。其余尺寸/字号 token 不动。

### 杠杆 3 · 预览缩放口径对齐

[HistoryPanel.cs:390-393](../../../Game/HistoryPanel/HistoryPanel.cs#L390-L393) `ComputePreviewPanelScale()`：
`Screen.width / 1920` → `Screen.height / 1080`，与杠杆 1 的按高匹配一致。叠加层位置/尺寸本就来自容器真实
矩形，所以容器一变即自动跟随；此处仅修正调参偏移（X/Y/W/H）所用的 ref-px→实际像素换算口径。

## 受影响文件

- [Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs)
  —— `PanelSettings.match`。
- [Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs)
  —— 去遮罩、面板铺满、直角、content/runs/preview 改相对。
- [Infrastructure/UiTokens/Sizes.cs](../../../Infrastructure/UiTokens/Sizes.cs)
  —— 移除/改造结构常量。
- [Game/HistoryPanel/HistoryPanel.cs](../../../Game/HistoryPanel/HistoryPanel.cs)
  —— `ComputePreviewPanelScale()` 改按高度。

## 风险

1. **`PanelSettings.match` API**：该 Unity 版本的确切字段/语义需反射核对（按 Managed DLL，而非
   `decompiled/`）。
2. **z 序穿透** ⭐：游戏自有 UI 若 `sortingOrder > 26`，全屏后会盖在面板之上、看起来像 bug
   （小盒子时在四周属正常）。需在能打开面板的场景（商店 / 地图 / 主菜单）现场确认 26/27 是否够高。
3. **percent-height 依赖父级确定高度**：`content` 在 100% 高的 `panel` 内 `flex-grow:1` 应满足；
   若 percent-height 不稳，退化为 `flex-basis`。
4. **调参器口径漂移**：`ComputePreviewPanelScale` 改按高度后，调参器里以「1080p 等效 px」表达的偏移
   语义随之变化；baked 默认值（X=4/Y=10/W=-8/H=-20，均为很小的内边距）在新口径下影响有限，但需现场目测。

## 验证

UI 布局改动、无自动化测试缝。按 [.rules](../../../.rules) 可不强制 `BuildAll`：

- **编译**（最小相关项目 / 主 mod 项目）。
- **游戏内三分辨率目测**：1080p / 21:9 超宽 / 4K —— 重点看：面板铺满不溢出、竖向密度一致、预览棋盘
  居中对位正确、无 z 序穿透、Close/Esc/热键正常、开战自动关闭正常。
