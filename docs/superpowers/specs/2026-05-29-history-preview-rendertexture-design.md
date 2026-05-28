# History Panel 预览改回 RenderTexture（贴进 UI Toolkit）

> 设计文档。把 HistoryPanel 的战斗板预览从「独立 ScreenSpaceOverlay Canvas + 手动坐标同步」
> 改成「离屏 Camera 渲染卡片到固定 RenderTexture + UI Toolkit Image 显示」。目标是让 UI Toolkit
> 接管所有缩放/分辨率适配，根除当前 overlay 方案切分辨率即失效、且需要手动 tune 的问题。

## Scope

只动 HistoryPanel 的 battle board 预览**渲染与显示通路**。不改预览的数据来源（`HistoryItemSpec`
列表）、不改卡片 prefab 反射（`HistoryPanelPreviewCardPool`）、不改面板布局结构（runs/battles
分栏、footer 等）。

## Background

### 当前实现（overlay Canvas）

[BattleBoardPreview.cs](../../../Game/HistoryPanel/Preview/BattleBoardPreview.cs) 自建一个
`RenderMode.ScreenSpaceOverlay` 的 Canvas（`sortingOrder = 27`），把卡片直接画在屏幕上，再靠
[HistoryPanel.cs](../../../Game/HistoryPanel/HistoryPanel.cs) 的 `ApplyPreviewContainerBounds`
把 UI Toolkit `previewContainer.worldBound` 翻译成 Canvas 的 screen-space 像素位置 / clip 尺寸 /
卡片缩放。

这套的结构性问题：**overlay Canvas 与 UI Toolkit 面板是两套坐标系，必须手动同步**。
- 切换分辨率 / 窗口尺寸时同步会失准（本次会话实测：切分辨率后预览错位）。
- 为了对齐，引入了 `_debugX/Y/WidthDelta/HeightDelta` + `ComputePreviewPanelScale`（= `Screen.width/1920`）
  的手动调参层（[HistoryPanel.PreviewTunerDebug.cs](../../../Game/HistoryPanel/HistoryPanel.PreviewTunerDebug.cs)，
  本次会话新增、**尚未 commit**），调起来繁琐且本质治标不治本。

### 这其实是「回到一个被废弃过的方案」

- `bdd3f62`「Migrate HistoryPanel preview to the game's native card rendering pipeline」**最初就是
  Camera + RenderTexture**：离屏 `ScreenSpaceCamera` Canvas + 正交 Camera 渲染卡片到 RT，并 wire
  `GeometryChangedEvent` 让 RT 跟随容器像素比例重建 —— 当时明确是为了解决宽屏 letterbox。
- `ce20923`「Improve UI typography and history previews」把整套 Camera+RT 删掉换成
  `ScreenSpaceOverlay`，**commit message 未记录原因**。

因此存在一个**历史风险**（见 §风险）：当初放弃 RT 可能是因为卡片渲染进离屏 RT 有问题。用户已知情并选择
重新走 RT，遇到渲染问题当场 debug。

### 已经留着的 RT 时代脚手架（无需新建）

- [HistoryPanelPreviewTextureGeometry.cs](../../../Game/HistoryPanel/Preview/HistoryPanelPreviewTextureGeometry.cs)：
  `NativeBoardWidth = 2400`、`NativeBoardHeight = 600`（4:1），以及 `ResolveTextureSize` /
  `ResolveBoardPlacement`（带 letterbox 居中）。目前只有两个常量在用。
- [HistoryPanelUiToolkitView.Tree.cs:224-231](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs#L224-L231)：
  `_previewImage = new Image()`，`scaleMode = ScaleMode.ScaleToFit`，挂在 `_previewContainer`
  （固定高度 `Sizes.PreviewHeight`、背景 `Colors.HistoryPreviewBackground`、`overflow = Hidden`）里。
- [HistoryPanelUiToolkitView.cs:229-237](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs#L229-L237)：
  `SetPreviewTexture(Texture?)` 已经把 texture 塞进 `_previewImage` 并切显示。当前 overlay 方案从不调它。

也就是说 **UI 显示侧整套现成，只是没人喂图**。

## Goal / 验收标准

1. 预览作为一张 RenderTexture 显示在 UI Toolkit `_previewImage`（`ScaleToFit`）里，由 UI Toolkit
   负责所有容器内缩放与居中。
2. 在 16:9 / 16:10 / 21:9 / 32:9 以及窗口拖动改尺寸时，预览都正确居中、不拉伸变形、不错位 —— **无需任何
   手动 tune 参数**。
3. RT 为固定原生比例（4:1），容器比例不符时由 `ScaleToFit` 信箱居中；信箱边为透明，露出容器背景
   `Colors.HistoryPreviewBackground`（Camera 透明清屏）。
4. 现有 `HistoryPanelPreview.Tests`（signature gate + generation guard）保持绿。

## Approach（方案 A：固定原生比例 RT + Image ScaleToFit，Camera 持续渲染）

新组件 `BattleBoardRenderTexturePreview`（取代 `BattleBoardPreview`），复用 `ce20923^` 旧 RT 版本被
验证过的 Camera/Canvas/RT 搭建：

- **离屏 root** GameObject，专用 layer（沿用 `30`），`SetActive(false)` 直到首次使用。
- **正交 Camera**：`cullingMask = 1 << layer`（只拍预览卡）、`clearFlags = SolidColor` +
  `backgroundColor = (0,0,0,0)`（透明清屏 → 信箱透明）、`orthographic = true`。**`enabled = true`
  持续渲染**（旧 RT 版是 `enabled=false` 截一帧的快照；本设计按用户选择改为实时）。仅面板可见时启用，
  隐藏即 `enabled=false`。
- **`ScreenSpaceCamera` Canvas**：`worldCamera = camera`，sockets 由
  [HistoryPanelPreviewLayout.BuildSockets](../../../Game/HistoryPanel/Preview/HistoryPanelPreviewLayout.cs)
  建在 canvas RectTransform 上。
- **固定 RT**：`2400×600`（ARGB32，含 depth，AA=2），`camera.targetTexture = rt`，
  `orthographicSize = height * 0.5f / 100f`（Canvas `referencePixelsPerUnit = 100`），
  `camera.aspect = width / height`。**永不按容器重建**。

整段复用、不动：`HistoryPanelPreviewCardPool`、`HistoryPanelPreviewLayout`、`HistoryPanelPreviewTemplateLookup`、
`HistoryPanelCardPreviewReflection`、`HistoryPanelPreviewGenerationGuard`、`HistoryPanelPreviewSignatureGate`、
`HistoryItemSpec`、`HistoryPanelPreviewTextureGeometry` 常量。

### 数据流

```
选中 battle
  → HistoryPanel 造 HistoryItemSpec 列表（不变）
  → renderer.Render(specs, signature)：sockets 内 spawn/复用卡，await SetUp（沿用 generation guard + signature gate）
  → Camera 每帧把 canvas 拍进固定 RT
  → view.SetPreviewTexture(rt)（RT 首次创建时调一次）
  → UI Toolkit Image(ScaleToFit) 负责所有缩放/信箱/分辨率适配
无选中 → 卡回收 + SetPreviewTexture(null)（Image 隐藏）
面板隐藏 → camera.enabled = false；Dispose → RT Release+Destroy、销毁 root
```

### 状态/错误处理

- 保留 `BattleBoardRenderPhase`（Empty / InitFailed / Loading / Done）→ HistoryPanel 现有状态文字映射不变。
- MonsterBoardTooltip 反射拿不到 prefab refs、或 `RenderTexture.Create()` 失败 → `InitFailed`。
- 单卡 `SetUp` 抛错 → 沿用 `InvokeSetUpSafe` 记 warn 并继续；signature 仅在聚合无 fault 时缓存（`SignatureGate`）。

## 要删除 / 回退的代码

- 删 [HistoryPanel.PreviewTunerDebug.cs](../../../Game/HistoryPanel/HistoryPanel.PreviewTunerDebug.cs)（整套 tuner，未 commit）。
- 回退 [HistoryPanel.cs](../../../Game/HistoryPanel/HistoryPanel.cs) 的 `ApplyPreviewContainerBounds`、
  `ComputePreviewPanelScale`，以及 `EnsurePreviewRenderer` / `OnPreviewContainerBoundsChanged` 里对它的调用。
- 删 overlay 版 [BattleBoardPreview.cs](../../../Game/HistoryPanel/Preview/BattleBoardPreview.cs)
  （`SetPosition` / `SetClipSize` / `SetCardScale` / `SetClipMaskEnabled` 随之消失）。
- 评估 `_previewContainerBounds` / `_hasPreviewContainerBounds` / `PreviewContainerBoundsChanged` 事件
  是否还有其它用途；固定 RT 方案不需要它，无其它消费者则一并移除。

## 实现起步（针对历史风险 de-risk）

1. **最小验证 spike**：只 spawn 一张卡，确认能正确拍进 RT 并在 `_previewImage` 里显示（比例对、不空白/不黑块）。
2. spike 通过 → 补齐 10 socket、可见性开关、SignatureGate 接线、删除旧 overlay/tuner 代码。
3. spike 若失败（撞上当初放弃 RT 的最可能原因）→ 立即回报，不闷头继续。

## 测试

- 保持 `HistoryPanelPreview.Tests`（signature gating + generation guard 这两个可抽取 seam）绿；不新增 mock/快照式无效测试（遵循 `.rules`）。
- 手动验证：16:9 / 21:9 / 窗口拖动改尺寸下，预览居中、不变形、不错位；切分辨率后仍正确。

## Out of scope

- 不解决 [history-panel-known-issues.md](../../history-panel-known-issues.md) #1 的根容器写死 `1280×1020` +
  `screenMatchMode` 未声明的宽屏溢出问题（那是面板整体布局，独立任务）。本设计只让**预览区**在容器内正确自适应。
- 不改预览数据投影、不改卡片反射管线、不改 Ghost/replay 逻辑。

## 风险

- **历史风险（最高）**：`ce20923` 放弃 RT 原因无记录，最可能是卡片（uGUI CardPreviewBase）渲染进离屏
  Camera/RT 有问题（空白 / 缺图 / 透明度 / 材质）。缓解：实现第一步即 spike 验证；失败立即回报。
- 实时 Camera 的额外开销：单个小正交 Camera、仅面板可见时渲染、固定小 RT，开销可忽略；用 `enabled` 严格门控。
