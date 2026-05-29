# History Panel — Known Issues

Tracking list of known visual / layout issues in the History Panel that have been
acknowledged but not yet fixed. Each entry should record symptom, root cause, and
the bar for "fixed."

---

## 1. 宽屏 / 超宽屏适配

**状态**：✅ 已解决（2026-05-29，全屏响应式改造）。

### 解决方案

面板从「屏幕居中的固定 1280×1020 盒子」改成「边到边全屏、按相对比例自适应」。落地于
[2026-05-29-historypanel-fullscreen-responsive-design.md](superpowers/specs/2026-05-29-historypanel-fullscreen-responsive-design.md)：

- **按高匹配**：`PanelSettings.match = 1f`（[HistoryPanelUiToolkitView.cs](../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs)），
  token 随屏幕高度缩放，竖向密度恒定、超宽屏不再竖向溢出。
- **flex 填满 + 百分比**（[HistoryPanelUiToolkitView.Tree.cs](../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs)）：
  去掉压暗遮罩，panel 铺满 root（`flexGrow:1`）、直角、背景不透明；content `flexGrow:1`；
  runs 栏 `width: Percent(30)`、battles `flexGrow:1`；preview 容器 `height: Percent(28)`。
  结构常量 `HistoryPanelWidth/Height/HistoryContentHeight/RunsColumnWidth/PreviewHeight` 已删除，
  改为比例常量 `RunsColumnWidthPercent=30`、`PreviewHeightPercent=28`。
- **预览口径对齐**：`ApplyPreviewContainerBounds` 退化为纯 `worldBound→clip` 映射；叠加层定位本就走
  `scaledPixelsPerPoint`，`match` 一变即自动跟随。删除了 `ComputePreviewPanelScale` 与 F9 调参器。

> 取舍：`match=1`（纯按高）而非旧建议的 `0.5`（折中）。对以竖向列表为主的全屏面板，按高更能保证
> 各分辨率下可见行数比例一致；代价是 32:9 等超宽屏两侧留白，已确认接受。

### 残留待现场确认（非阻塞）

- **z 序穿透**：面板 `sortingOrder = 26`、预览叠加层 `27`。小盒子时游戏 UI 露在四周属正常；全屏后若
  游戏自有 UI `sortingOrder > 26`，会盖在面板之上。需在商店 / 地图 / 主菜单现场确认是否够高。

---

## 2. 预览渲染管线：overlay 而非 RenderTexture（已决策）

**状态**：已决策，勿再提议改回 RT。

战斗板预览维持独立 `ScreenSpaceOverlay` Canvas（[BattleBoardPreview.cs](../Game/HistoryPanel/Preview/BattleBoardPreview.cs)）+
读容器 `worldBound` 同步坐标，而非「离屏 Camera 渲染卡片到 RenderTexture 再贴进 UI Toolkit Image」。

曾实现过 RT 方案（离屏 Camera 渲染卡片到 RenderTexture 再贴进 UI Toolkit Image），但**在 URP 下
离屏 Camera→RT 无法渲染 uGUI（CardPreviewBase）**，已回退到 overlay 方案。
`HistoryPanelPreviewTextureGeometry` 只剩 `NativeBoardWidth/Height` 两个常量供 overlay 路径使用；
RT 时代的 `ResolveTextureSize/ResolveBoardPlacement` 及其测试已随回退删除。

---

## 历史条目（已解决，留档）

### 宽屏 / 超宽屏适配 — 原始记录

> 以下为修复前的症状 / 根因记录，留作背景。

#### 症状

在非 16:9 的分辨率（典型场景：21:9 / 32:9 超宽屏、便携机的窄屏、窗口模式下被手动缩小的画布）下，History Panel：

- 面板本体相对屏幕大小不正确（过大、过小、或被裁切）
- 内部分栏（runs / battles / preview）比例失衡
- Preview 区域固定 RenderTexture 比例与实际显示框不一致，导致 letterbox 或拉伸
- 个别行内 chip / button 因为按像素硬编码宽度，在缩放后出现折行或撑出容器

#### 根因

1. **面板根容器写死像素尺寸**：[HistoryPanelUiToolkitView.Tree.cs:23-24](../Game/HistoryPanel/HistoryPanelUiToolkitView.Tree.cs#L23-L24)

   ```csharp
   panel.style.width = 1280f;
   panel.style.height = 1020f;
   ```

   面板既没有 `maxWidth` / `maxHeight`，也没有按百分比布局。当 `PanelSettings.scaleMode = ScaleWithScreenSize`（[HistoryPanelUiToolkitView.cs:89](../Game/HistoryPanel/HistoryPanelUiToolkitView.cs#L89)）配合 `referenceResolution = (1920, 1080)` 时，非 16:9 屏会按宽或高单边缩放，超宽屏上很容易把 1020px 高度顶出可视区。

2. **`screenMatchMode` 未显式设置**：依赖 Unity 的默认值（width-matched），在 21:9 超宽屏上行为未定义良好。

3. **Preview 区域比例锁定**：当时的 RT 渲染器（后已删除的 `HistoryPanelPreviewRenderer.cs`）把纹理宽高写死成固定比例；UI 容器若按屏幕缩放，会和这个比例错位。

4. **行内组件像素硬编码**：已通过 UI token 基础设施降级；chip / pill / button / bubble 的固定尺寸现在走 `Infrastructure/UiTokens/Sizes.cs`。剩余宽屏风险集中在 1/2/3 项。

#### 验收标准（修复时必须满足）

- 在 16:9 / 16:10 / 21:9 / 32:9 四档常见分辨率下，面板：
  - 完整可见（不溢出屏幕、不被裁切）
  - 关键三栏 (runs / battles / preview) 比例与 16:9 下视觉一致
  - 按钮、chip、pill 不折行、不被遮挡
- Preview RenderTexture 的宽高比能跟随容器变化（或在容器内 letterbox 居中，且边距与面板背景同色，不出现明显黑边）
- `PanelSettings.screenMatchMode` 显式声明（推荐 `MatchWidthOrHeight` + match=0.5）
- 至少一组截图（两端极端比例）附在 PR 描述里作为证据

#### 备选实现思路

- 把根 panel 改成 `width = Length.Percent(80)` + `maxWidth = 1600px` + `minWidth = 1100px`，高度同理
- 子栏改用 `flexGrow` + `flexBasis` 描述比例，而不是各栏自己写死 width
- Preview RenderTexture 改为按容器实际像素尺寸重建（容器变化时 Release+Create，加防抖避免拖动时频繁重分配）

#### 相关讨论

最早在分析 HistoryPanel 架构时被识别（参见 `senior engineer review` 一节，1280×1020 fixed-root + ScaleWithScreenSize 的组合问题）。
