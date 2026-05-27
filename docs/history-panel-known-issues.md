# History Panel — Known Issues

Tracking list of known visual / layout issues in the History Panel that have been
acknowledged but not yet fixed. Each entry should record symptom, root cause, and
the bar for "fixed."

---

## 1. 宽屏 / 超宽屏适配 (next-version blocker)

**状态**：已知问题，下一个版本必须解决。

### 症状

在非 16:9 的分辨率（典型场景：21:9 / 32:9 超宽屏、便携机的窄屏、窗口模式下被手动缩小的画布）下，History Panel：

- 面板本体相对屏幕大小不正确（过大、过小、或被裁切）
- 内部分栏（runs / battles / preview）比例失衡
- Preview 区域固定 RenderTexture 比例与实际显示框不一致，导致 letterbox 或拉伸
- 个别行内 chip / button 因为按像素硬编码宽度，在缩放后出现折行或撑出容器

### 根因

1. **面板根容器写死像素尺寸**：[HistoryPanelUiToolkitView.Tree.cs:23-24](../Game/HistoryPanel/HistoryPanelUiToolkitView.Tree.cs#L23-L24)

   ```csharp
   panel.style.width = 1280f;
   panel.style.height = 1020f;
   ```

   面板既没有 `maxWidth` / `maxHeight`，也没有按百分比布局。当 `PanelSettings.scaleMode = ScaleWithScreenSize`（[HistoryPanelUiToolkitView.cs:89](../Game/HistoryPanel/HistoryPanelUiToolkitView.cs#L89)）配合 `referenceResolution = (1920, 1080)` 时，非 16:9 屏会按宽或高单边缩放，超宽屏上很容易把 1020px 高度顶出可视区。

2. **`screenMatchMode` 未显式设置**：依赖 Unity 的默认值（width-matched），在 21:9 超宽屏上行为未定义良好。

3. **Preview 区域比例锁定**：[HistoryPanelPreviewRenderer.cs:21-22](../Game/HistoryPanel/HistoryPanelPreviewRenderer.cs#L21-L22) 的 `TextureWidth = 2688`、`TextureHeight = 640` 写死了 4.2:1 的渲染宽高比；UI 容器若按屏幕缩放，会和这个比例错位。

4. **行内组件像素硬编码**：已通过 UI token 基础设施降级；chip / pill / button / bubble 的固定尺寸现在走 `Infrastructure/UiTokens/Sizes.cs`。剩余宽屏风险集中在 1/2/3 项。

### 验收标准（修复时必须满足）

- 在 16:9 / 16:10 / 21:9 / 32:9 四档常见分辨率下，面板：
  - 完整可见（不溢出屏幕、不被裁切）
  - 关键三栏 (runs / battles / preview) 比例与 16:9 下视觉一致
  - 按钮、chip、pill 不折行、不被遮挡
- Preview RenderTexture 的宽高比能跟随容器变化（或在容器内 letterbox 居中，且边距与面板背景同色，不出现明显黑边）
- `PanelSettings.screenMatchMode` 显式声明（推荐 `MatchWidthOrHeight` + match=0.5）
- 至少一组截图（两端极端比例）附在 PR 描述里作为证据

### 备选实现思路

- 把根 panel 改成 `width = Length.Percent(80)` + `maxWidth = 1600px` + `minWidth = 1100px`，高度同理
- 子栏改用 `flexGrow` + `flexBasis` 描述比例，而不是各栏自己写死 width
- Preview RenderTexture 改为按容器实际像素尺寸重建（容器变化时 Release+Create，加防抖避免拖动时频繁重分配）

### 相关讨论

最早在分析 HistoryPanel 架构时被识别（参见 `senior engineer review` 一节，1280×1020 fixed-root + ScaleWithScreenSize 的组合问题）。
