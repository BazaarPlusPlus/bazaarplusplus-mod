# Monster Preview

## Scope

本文只描述当前 shipped 的怪物预览实现。旧的 Bazaar++ 自绘 monster showcase 路径、锁定野怪后弹出的 Bazaar++ item-board overlay、对原生 monster tooltip 的 augment 注入全部已被移除，当前运行时保留的是：

- `CardSetPreviewRuntime` 通过 `GameInterop/ItemBoardPreview` 渲染 CardSet preview overlay，不再创建或克隆真实 `MonsterBoardTooltip`
- `HistoryPanel` 的战斗板预览通过 `Game/HistoryPanel/Preview/BattleBoardPreview.cs` 包一层同一个 shared surface（与怪物预览无关）

## Runtime Entry

`Plugin.cs` 当前挂载的怪物预览相关运行时：

- `CardSetPreviewRuntime`

## 当前主路径

默认的野怪预览展示完全走游戏原生 monster preview，Bazaar++ 不做任何介入。历史上曾通过 `NativeMonsterPreviewTooltipPatch` + `NativeMonsterTooltipAugmenter` 在 `CardController.ShowTooltips()` / `GetTooltipData()` 前后注入 monster 上下文，这部分代码已被删除。

## 相关旁路

### Item-board overlay

`CardSetPreviewRuntime` 把 Bazaar++ 自己组织的 item set 交给共享 item-board surface。该 surface 复用原生 `CardPreviewBase` prefab，直接在 `ScreenSpaceOverlay` canvas 内渲染 10-slot board：

```text
CardSetPreviewRuntime
  -> ItemBoardService
  -> GameInterop/ItemBoardPreview/ItemBoardPreviewSurface
  -> GameInterop/CardPreview/NativeCardPreviewFactory
```

这条路径用于内容推荐展示，不是旧的 monster self-render showcase。CardSet 的候选状态、模式文案和 sponsor chrome 仍留在 `Game/CardSetPreview/`。

### History Panel

`HistoryPanel` 的战斗板预览与怪物预览完全解耦；`BattleBoardPreview` 只负责把 `HistoryItemSpec` 映射成 shared surface 的 `NativeCardPreviewSpec`，并读 UI Toolkit
预览容器的 `worldBound` 同步位置。曾短暂改用离屏 Camera→RenderTexture，
因 URP 下无法渲染 uGUI 已回退到 overlay；详见 [history-panel.md](history-panel.md) §预览渲染 与 [ADR-0003](../adr/0003-history-panel-preview-overlay.md)。

History preview 过滤 card template 时使用游戏静态数据
`Data.GetStatic().GetCardById(Guid)`。Bazaar++ 不再定位、解析、缓存或预热本地卡牌模板
JSON；过期或未知的 template id 会在渲染前被过滤掉。

## 关键文件

- `Plugin.cs`
- `Game/CardSetPreview/CardSetPreviewRuntime.cs`
- `Game/CardSetPreview/ItemBoardService.cs`
- `GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs`
- `GameInterop/CardPreview/NativeCardPreviewFactory.cs`
- `Game/HistoryPanel/Preview/BattleBoardPreview.cs`
- `Game/HistoryPanel/HistoryPanelPreviewSource.cs`

## Debug

- Bazaar++ 不再 patch 原生 monster tooltip 流程；CardSet preview / item-board 相关问题看 `CardSetPreviewRuntime`、`ItemBoardService` 和 `ItemBoardPreviewSurface` 的日志。
- `HistoryPanel` 预览问题看 `BattleBoardPreview` 与 `ItemBoardPreviewSurface`。
