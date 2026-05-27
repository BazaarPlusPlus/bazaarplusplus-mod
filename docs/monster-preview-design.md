# Monster Preview

## Scope

本文只描述当前 shipped 的怪物预览实现。旧的 Bazaar++ 自绘 monster showcase 路径、锁定野怪后弹出的 Bazaar++ item-board overlay、对原生 monster tooltip 的 augment 注入全部已被移除，当前运行时保留的是：

- `CardSetPreviewRuntime` 复用原生 `MonsterBoardTooltip` 作为宿主渲染 CardSet preview overlay
- `HistoryPanel` 使用的共享 `PreviewSurface`

## Runtime Entry

`Plugin.cs` 当前挂载的怪物预览相关运行时：

- `CardSetPreviewRuntime`

## 当前主路径

默认的野怪预览展示完全走游戏原生 monster preview，Bazaar++ 不做任何介入。历史上曾通过 `NativeMonsterPreviewTooltipPatch` + `NativeMonsterTooltipAugmenter` 在 `CardController.ShowTooltips()` / `GetTooltipData()` 前后注入 monster 上下文，这部分代码已被删除。

## 相关旁路

### Item-board overlay

`CardSetPreviewRuntime` 仍会复用游戏原生 `MonsterBoardTooltip` 作为宿主，克隆一份 tooltip view，再把 Bazaar++ 自己组织的 item set 渲染进去：

```text
CardSetPreviewRuntime
  -> ItemBoardService
  -> ItemBoardOverlay
  -> cloned MonsterBoardTooltip
```

这条路径用于内容推荐展示，不是旧的 monster self-render showcase。

### History Panel

`HistoryPanel` 仍然使用 Bazaar++ 的自绘 preview surface，但它走的是共享的 `Game/PreviewSurface` 渲染栈，与旧 monster showcase 已经解耦。

History preview 过滤 card template 时使用游戏静态数据
`Data.GetStatic().GetCardById(Guid)`。Bazaar++ 不再定位、解析、缓存或预热本地卡牌模板
JSON；过期或未知的 template id 会在进入 preview surface 前被过滤掉。

## 关键文件

- `Plugin.cs`
- `Game/CardSetPreview/CardSetPreviewRuntime.cs`
- `Game/CardSetPreview/ItemBoardOverlay.cs`
- `Game/HistoryPanel/HistoryPanelPreviewRenderer.cs`
- `Game/HistoryPanel/HistoryPanelRepository.Preview.cs`
- `Game/PreviewSurface/Board/PreviewBoardSurface.cs`

## Debug

- Bazaar++ 不再 patch 原生 monster tooltip 流程；CardSet preview / item-board 相关问题看 `CardSetPreviewRuntime`、`ItemBoardOverlay` 的日志。
- `HistoryPanel` 预览问题看 `HistoryPanelPreviewRenderer` 与 `PreviewBoardRenderTarget`。
