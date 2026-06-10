---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Monster Preview

## Scope

本文只描述当前 shipped 的怪物预览实现。旧的 Bazaar++ 自绘 monster showcase 路径、锁定野怪后弹出的 Bazaar++ item-board overlay、对原生 monster tooltip 的 augment 注入全部已被移除，当前运行时保留的是：

- `LiveBuildPanel` 通过 `GameInterop/ItemBoardPreview` 渲染终局阵容 item-board overlay，不再创建或克隆真实 `MonsterBoardTooltip`
- `HistoryPanel` 的战斗板预览也走同一个 shared socketed surface（与怪物预览无关）

## Runtime Entry

`BppComposition.cs` 当前挂载的 item-board 相关运行时：

- `LiveBuildPanelMount`
- `HistoryPanelMount`

## 当前主路径

默认的野怪预览展示完全走游戏原生 monster preview，Bazaar++ 不做任何介入。历史上曾通过 `NativeMonsterPreviewTooltipPatch` + `NativeMonsterTooltipAugmenter` 在 `CardController.ShowTooltips()` / `GetTooltipData()` 前后注入 monster 上下文，这部分代码已被删除。

## 相关旁路

### Item-board overlay

`LiveBuildPanel` 把 Bazaar++ 组织的实时 shop / board / stash / final-build recommendation 交给共享 item-board surface。该 surface 复用原生 `CardPreviewBase` prefab，直接在 `ScreenSpaceOverlay` canvas 内渲染 10-slot board：

```text
LiveBuildPanel
  -> BppItemBoardPreview
  -> GameInterop/ItemBoardPreview/ItemBoardPreviewSurface
  -> GameInterop/CardPreview/NativeCardPreviewFactory
```

实际中间层：`LiveBuildPanel`（`LiveBuildPanel.cs:35`）持有 `LiveBuildPreviewRenderer`，`LiveBuildPreviewRenderer` 通过 `LiveItemBoardRowPreview`（`LiveItemBoardRowPreview.cs:11`）持有 `BppItemBoardPreview`。

这条路径用于 live run 内容推荐展示，不是旧的 monster self-render showcase。候选状态、面板文案和 supporter attribution chrome 位于 `Game/LiveBuildPanel/`。

### History Panel

`HistoryPanel` 的战斗板预览与怪物预览完全解耦；历史快照被投影成 `BppItemBoard`，再由 `BppItemBoardPreview` 映射成 shared surface 的 `NativeCardPreviewSpec`，并读 UI Toolkit
预览容器的 `worldBound` 同步位置。曾短暂改用离屏 Camera→RenderTexture，
因 URP 下无法渲染 uGUI 已回退到 overlay；详见 [history-panel.md](history-panel.md) §预览渲染 与 [ADR-0003](../../adr/0003-history-panel-preview-overlay.md)。

History preview 过滤 card template 时经 `GameInterop/StaticCards/BppStaticDataAccess` 间接访问游戏静态数据（内部调 `manager.GetCardById(templateId)`，并处理 `Data.GetStatic()` 返回 Task 的版本差异；`HistoryBattlePreviewProjection.cs` 在 `TryGetStaticGameData()` 中调用 `BppStaticDataAccess.TryGet()`）。Bazaar++ 不再定位、解析、缓存或预热本地卡牌模板 JSON；过期或未知的 template id 会在渲染前被过滤掉。

## 关键文件

- `Plugin.cs`
- `BppComposition.cs`
- `Game/LiveBuildPanel/LiveBuildPanel.cs`
- `GameInterop/ItemBoardPreview/BppItemBoardPreview.cs`
- `GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs`
- `GameInterop/CardPreview/NativeCardPreviewFactory.cs`
- `Game/HistoryPanel/HistoryPanel.cs`
- `Game/HistoryPanel/HistoryPanelPreviewSource.cs`

## Debug

- Bazaar++ 不再 patch 原生 monster tooltip 流程；LiveBuildPanel / item-board 相关问题看 `LiveBuildPanel`、`LiveCardSnapshotReader` 和 `ItemBoardPreviewSurface` 的日志。
- `HistoryPanel` 预览问题看 `HistoryPanelPreview` 与 `ItemBoardPreviewSurface`。
