# Monster Preview

## Scope

本文只描述当前 shipped 的怪物预览实现。

2026-04 的清理中，旧的 Bazaar++ 自绘 monster showcase 路径已经删除。当前运行时保留的是：

- 原生怪物 tooltip / monster board 的局部增强
- 基于原生 `MonsterBoardTooltip` 的 Bazaar++ item-board overlay
- `HistoryPanel` 使用的共享 `PreviewSurface`

## Runtime Entry

`Plugin.cs` 当前挂载的怪物预览相关运行时：

- `MonsterPreviewWarmupController`
- `CardSetPreviewRuntime`
- `MonsterPreviewItemBoardRuntime`

## 当前主路径

默认的野怪预览展示走游戏原生 monster preview。

Bazaar++ 只在原生路径前后做局部注入：

```text
CardController.ShowTooltips() / GetTooltipData()
  -> NativeMonsterPreviewTooltipPatch
  -> NativeMonsterTooltipAugmenter
  -> 原生 CardTooltip / MonsterBoardTooltip
```

这里的 augment 是兜底式补充：仅当 `CardTooltipData` 尚未带 monster 上下文时才会补，不会覆盖已有原生数据。

## 相关旁路

### Item-board overlay

`MonsterPreviewItemBoardRuntime` 和 `CardSetPreviewRuntime` 仍会复用游戏原生 `MonsterBoardTooltip` 作为宿主，克隆一份 tooltip view，再把 Bazaar++ 自己组织的 item set 渲染进去：

```text
CardSetPreviewRuntime / MonsterPreviewItemBoardRuntime
  -> ItemBoardService
  -> ItemBoardOverlay
  -> cloned MonsterBoardTooltip
```

这条路径用于内容推荐和 board-only 展示，不是旧的 monster self-render showcase。

### History Panel

`HistoryPanel` 仍然使用 Bazaar++ 的自绘 preview surface，但它走的是共享的 `Game/PreviewSurface` 渲染栈，与旧 monster showcase 已经解耦。

History preview 过滤 card template 时使用游戏静态数据
`Data.GetStatic().GetCardById(Guid)`。Bazaar++ 不再定位、解析、缓存或预热本地卡牌模板
JSON；过期或未知的 template id 会在进入 preview surface 前被过滤掉。

## 关键文件

- `Plugin.cs`
- `Game/MonsterPreview/MonsterPreviewWarmupController.cs`
- `Game/MonsterPreview/MonsterPreviewItemBoardRuntime.cs`
- `Game/MonsterPreview/CardSetPreviewRuntime.cs`
- `Patches/Tooltips/NativeMonsterPreviewTooltipPatch.cs`
- `Game/Tooltips/NativeMonsterTooltipAugmenter.cs`
- `Game/ItemBoard/ItemBoardOverlay.cs`
- `Game/HistoryPanel/HistoryPanelPreviewRenderer.cs`
- `Game/HistoryPanel/HistoryPanelRepository.Preview.cs`
- `Game/PreviewSurface/Board/PreviewBoardSurface.cs`

## Debug

- 怪物 tooltip / item-board 问题优先看 `NativeMonsterPreviewTooltipPatch`、`MonsterPreviewItemBoardRuntime`、`ItemBoardOverlay` 的日志。
- `HistoryPanel` 预览问题看 `HistoryPanelPreviewRenderer` 与 `PreviewBoardRenderTarget`。
