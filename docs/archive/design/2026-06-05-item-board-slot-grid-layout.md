---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

Status: IMPLEMENTED (historical) - code, tests, and Debug build passed; in-game visual validation pending

# Item Board 10-Slot Grid Layout

Date: 2026-06-05
Scope: `LiveBuildPanel` 四行 item board、共享 `BppItemBoardPreview`、以及 `HistoryPanel` preview 的 slot 对齐。

## 背景

当前共享 item-board 数据模型已经是 10-slot。`ItemBoardSocketLayout` 定义
`SocketCount = 10`，并保留一个 native board 常量 `2600x600`。Evidence:
[`ItemBoardSocketLayout.cs:7-11`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardSocketLayout.cs#L7-L11).

卡牌 span 语义也已经显式化：small = 1、medium = 2、large = 3。Evidence:
[`BppItemBoardSpan.cs:9-21`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardSpan.cs#L9-L21).

这和游戏自己的容器语义一致。decompiled `SocketedContainer` 会把同一张 item 写入它占用的每一个 socket，
但 `GetCardsAndSockets()` 只返回每张 item 的左 socket，并按 `socketable.Size - 1`
跳过后续占用格。Evidence:
[`SocketedContainer.cs:139-149`](../../../decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards/SocketedContainer.cs#L139-L149),
[`SocketedContainer.cs:198-210`](../../../decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards/SocketedContainer.cs#L198-L210).
客户端 `CardContainer.ApplyMetadata(...)` 也是在左 socket 写 `LeftSocketId`，再按 `Size - 1` 前进。
Evidence: [`CardContainer.cs:17-27`](../../../decompiled/BazaarGameClient/BazaarGameClient.Domain.Cards/CardContainer.cs#L17-L27).

所以本方案的目标 contract 是：

- 所有 item board row 都是 10 个逻辑 slot。
- 一张 item 占用 `[DisplaySocketId, DisplaySocketId + DisplaySpan)`。
- 小/中/大卡分别在 1/2/3 个 slot 的占用区域里居中展示。
- shop 行先计算自己居中的 display slot，再和 board/stash/history 使用同一套 grid renderer。
- slot 背板只是可选视觉辅助，不是另一套布局系统。

## 当前问题

`LiveBuildPanel` 的 UITK 层已经按整行 10 等分绘制 slot 背板、点击层和候选 marker：

- 已修复：slot 背板使用 `left = i * 10%`、`width = 10%`（`LiveBuildPanelView` 已用 `ItemBoardSlotGridGeometry.ResolveOccupiedRect`）。Evidence:
  [`LiveBuildPanelView.cs:228-240`](../../../src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs#L228-L240).
- hit target 使用 `left = socket * 10%`、`width = span * 10%`。Evidence:
  [`LiveBuildPanelView.cs:321-358`](../../../src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs#L321-L358).
- candidate marker 使用 `hostBounds.width / 10f` 计算 slot 宽。Evidence:
  [`LiveBuildPanelView.cs:370-390`](../../../src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs#L370-L390).

但 native card overlay 没有使用这套全宽 10 等分坐标。它先把一个固定 `2600x600` 的 board 等比 fit
到 row bounds 里：

- `LiveItemBoardRowPreview.SetBounds()` 计算 `scale = min(bounds.width / 2600, bounds.height / 600)`。
  Evidence:
  [`LiveItemBoardRowPreview.cs:44-50`](../../../src/BazaarPlusPlus/Game/LiveBuildPanel/Preview/LiveItemBoardRowPreview.cs#L44-L50).
- `ItemBoardPreviewSurface.ApplyTransform()` 把 `2600x600` 的 `_boardRect` 居中放进 clip，再应用 `_cardScale`。
  Evidence:
  [`ItemBoardPreviewSurface.cs:359-367`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L359-L367).

这就产生了两套坐标系：

1. UITK chrome 把整个 `slotHost` 宽度当作 10 个 slot。
2. native card art 把一个固定宽高比 board 居中塞进同一个 clip。

只要 row 实际宽高比不是 `2600:600`，native card art 就会和 UITK slot 背板、marker、hit target 错位。

还有一个数据到渲染的缺口：`BppItemBoardCard.DisplaySpan` 已经存在，slot planner 也会写
`DisplaySocketId`，但 mapper 目前只把 socket 传给 `NativeCardPreviewSpec`。Evidence:
[`BppItemBoardCard.cs:31-33`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardCard.cs#L31-L33),
[`BppItemBoardPreviewMapper.cs:23-33`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardPreviewMapper.cs#L23-L33).
`NativeCardPreviewSpec` 当前没有 display span 字段。Evidence:
[`NativeCardPreviewSpec.cs:9-22`](../../../src/BazaarPlusPlus/GameInterop/CardPreview/NativeCardPreviewSpec.cs#L9-L22).

已修复：`BppItemBoardPreview.Render()` 原忽略构造时传入的 layout mode 并硬编码 `Socketed`；`OptionsForwarder` 透传 `LayoutMode` 已落地。Evidence:
[`BppItemBoardPreview.cs:33-44`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardPreview.cs#L33-L44).

## 目标行为

### 单一 grid 坐标

row 的布局源就是可见 row 宽度，直接分成 10 个等宽逻辑 slot。native preview、透明 hit target、
candidate marker、可选 slot 背板都必须使用同一套 slot geometry。

renderer 不能在 planner 已经分配 `DisplaySocketId` 之后再根据 card frame 宽度做二次 packed。
当前 packed path 会测量 `FrameContainer` world width，然后把所有 active card 按 frame 宽度连续排在 board
中心。Evidence:
[`ItemBoardPreviewSurface.cs:412-443`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L412-L443).
这个行为不是 LivePanel / HistoryPanel 的 slot-fidelity 目标。

### Shop 行

shop 行仍然是 10-slot board，只是 shop selection 中的 item 通常没有真实 container socket。
它的居中 display socket 由 `BppItemBoardSlotPlanner` 计算，不由 renderer 现场 packed：

- `SelectableShop` 会进入 `PlanSelectableShop`。Evidence:
  [`BppItemBoardSlotPlanner.cs:19-24`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardSlotPlanner.cs#L19-L24).
- `PlanSelectableShop()` 会按来源顺序计算 `totalSpan`，能放下时从 `floor((10 - totalSpan) / 2)`
  开始，然后按每张卡的 span 递增分配 `DisplaySocketId`。Evidence:
  [`BppItemBoardSlotPlanner.cs:107-133`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardSlotPlanner.cs#L107-L133).

renderer 只消费这些 planned `DisplaySocketId`。它不能再按 frame width 对 shop 卡做一次居中。

### Board / Stash / History / Final Build

`SelectableContainer` 行保留合法 source socket，缺 socket 或越界的卡跳过。Evidence:
[`BppItemBoardSlotPlanner.cs:75-104`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardSlotPlanner.cs#L75-L104).

`Reference` 行优先保留 source/recommendation/snapshot 提供的 socket；缺 source socket 时才用
span-aware fallback。Evidence:
[`BppItemBoardSlotPlanner.cs:27-73`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardSlotPlanner.cs#L27-L73).

HistoryPanel 当前已经把 battle snapshot 投影成 `BppItemBoard(Id=Historical, Type=Reference)`。
Evidence:
[`HistoryBattlePreviewProjection.cs:55-67`](../../../src/BazaarPlusPlus/Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs#L55-L67).
它也应该复用同一个 slot-grid renderer，但默认不显示可见 slot 背板，避免改动 HistoryPanel 的视觉重心。

### 卡牌尺寸

native card art 应在占用 slot 矩形内居中，并做等比缩放。不要把 native card 非等比拉伸成任意宽高。
游戏的 `CardPreviewItem.Resize()` 会基于 prefab 的 layout 尺寸设置 rect。Evidence:
[`CardPreviewItem.cs:31-48`](../../../decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewItem.cs#L31-L48).

可调参数应该是：

- 占用 slot rect 内的水平 / 垂直 inset；
- 相对 row 高度的最大高度比例；
- 可选 max scale / min scale clamp；
- 固定 center alignment。

## 具体方案

### 1. 新增 SlotGrid layout mode

扩展 [`ItemBoardPreviewLayoutMode.cs`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardPreviewLayoutMode.cs#L4):

```csharp
internal enum ItemBoardPreviewLayoutMode
{
    Socketed,
    Packed,
    SlotGrid,
}
```

`SlotGrid` 的语义：

- socket index 决定逻辑占用范围的左边界；
- span 决定占用宽度；
- native card art 在占用范围内居中等比展示；
- 不允许 post-setup packed relayout。

### 2. 把 DisplaySpan 传到 preview spec

在 [`NativeCardPreviewSpec.cs`](../../../src/BazaarPlusPlus/GameInterop/CardPreview/NativeCardPreviewSpec.cs#L9) 增加：

```csharp
public int DisplaySpan { get; init; } = 1;
```

然后更新 [`BppItemBoardPreviewMapper.cs`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardPreviewMapper.cs#L23):

```csharp
DisplaySpan = card.DisplaySpan,
```

span 的来源仍然是 `BppItemBoardCard.DisplaySpan`。mapper 只负责把已规划好的 display contract 传进渲染层。

### 3. 修正 BppItemBoardPreview 的 LayoutMode 转发

更新 [`BppItemBoardPreview.Render()`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardPreview.cs#L33)，让转发给
surface 的 options 使用 `_options.LayoutMode`，而不是硬编码 `Socketed`。

这是 `SlotGrid` 能被 LivePanel / HistoryPanel opt-in 的前置修复。

### 4. 增加纯 slot-grid geometry helper

新增 `GameInterop/ItemBoardPreview/ItemBoardSlotGridGeometry.cs`。尽量保持 Unity-free，或者至少只依赖
简单数值类型，便于 exe-runner 单测覆盖。

建议接口：

```csharp
ResolveOccupiedRect(
    float rowWidth,
    float rowHeight,
    int socketIndex,
    int span,
    float horizontalInset,
    float verticalInset
)
```

行为：

- socket clamp 到 `[0, 9]`；
- span clamp 到 `[1, 10]`；
- end clamp 到 slot 10；
- `slotWidth = rowWidth / 10`；
- `left = socketIndex * slotWidth`；
- `width = min(span, 10 - socketIndex) * slotWidth`；
- 在得到逻辑占用范围后再应用 inset。

这个 helper 应成为 renderer placement tests 的数值 source。后续如果要收敛 UITK marker/backdrop
计算，也应复用同一套规则。

### 5. 在 ItemBoardPreviewSurface 实现 LayoutCardsSlotGrid()

在 [`ItemBoardPreviewSurface.cs`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L159) 的 layout
分支中加入：

```csharp
if (_options.LayoutMode == ItemBoardPreviewLayoutMode.SlotGrid)
    LayoutCardsSlotGrid();
else if (_options.LayoutMode == ItemBoardPreviewLayoutMode.Packed)
    LayoutCardsPacked();
```

`LayoutCardsSlotGrid()` 的步骤：

1. 对每个 active handle，从 `handle.Spec.SocketId` 取 `socketIndex`。
2. 从 `handle.Spec.DisplaySpan` 取 `span`。
3. 用完整 `_clipSize.width / 10` 解析该卡的占用 rect。
4. 测量 card visual bounds，优先沿用 packed path 里查找的 `FrameContainer`。Evidence:
   [`ItemBoardPreviewSurface.cs:424-427`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L424-L427).
5. 根据 occupied rect、inset、max-height ratio 计算等比 scale。
6. 应用 transform，使 measured frame center 对齐 occupied rect center。

这个 layout 必须在 setup/show 后运行，因为 native frame/art 尺寸只有在 setup 完成后才可靠。当前 render flow
已经等待 `Task.WhenAll(_activeSetUpTasks)`，show setup cards，`ForceUpdateCanvases()`，然后才做 layout。
Evidence:
[`ItemBoardPreviewSurface.cs:145-162`](../../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L145-L162).

### 6. 给 SlotGrid 加可调参数

在 `ItemBoardPreviewOptions` 增加代码级 tuning knobs：

```csharp
public float SlotGridHorizontalInsetPixels { get; init; } = 8f;
public float SlotGridVerticalInsetPixels { get; init; } = 6f;
public float SlotGridMaxHeightRatio { get; init; } = 0.96f;
public float SlotGridMaxScale { get; init; } = 1f;
```

第一版不接 settings UI。先通过代码常量和 Steam 手测确定合适的默认值。

### 7. LiveBuildPanel 切到 SlotGrid

修改 [`LiveItemBoardRowPreview`](../../../src/BazaarPlusPlus/Game/LiveBuildPanel/Preview/LiveItemBoardRowPreview.cs#L17)，把 layout mode
从 `Socketed` 改为 `SlotGrid`。

`LiveBuildPanel` 已经构造四个 `BppItemBoard` row：final build、live shop、live board、live stash。
Evidence:
[`LiveBuildPanel.cs:227-266`](../../../src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs#L227-L266).
live row 当前也已经通过 `BppItemBoardSlotPlanner.Plan(...)` 规划 display socket。Evidence:
[`LiveBuildPanel.cs:288-297`](../../../src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs#L288-L297).

不要给 renderer 增加 shop 特判。shop 居中留在 `BppItemBoardSlotPlanner.PlanSelectableShop()`。

### 8. slot 背板显隐变成显式选项

在 `LiveBuildPanelView` 保留当前 10-slot 背板，但把它变成显式视觉选项：

```csharp
private const bool ShowSlotBackdrop = true;
```

如果 playtest 需要快速切换，也可以做成 view constructor option。

关闭时不创建 visible slot backdrop，或者设置 `DisplayStyle.None`。不要关闭 hit targets 或 candidate markers：
它们是交互/选择表面，不是装饰线。

slot 背板是 LivePanel-specific chrome，不应下沉到 `GameInterop/ItemBoardPreview`。`GameInterop` 负责
native preview runtime/layout，候选样式、背板样式、面板 chrome 属于 `Game/LiveBuildPanel`。

### 9. HistoryPanel 切到 SlotGrid，但默认无背板

已完成：`HistoryPanel.cs:364` 已配置 `LayoutMode = SlotGrid`。Evidence:
[`HistoryPanel.cs:357-368`](../../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs#L357-L368).

HistoryPanel 现在与 LivePanel 使用同一个 "left socket + span" 视觉 contract，默认不添加可见 10-slot 背板。待游戏内手测验证。

## 测试

### 保留现有 planner 测试

`tests/HistoryPanelPreview.Tests` 已经覆盖 slot planner：

- `Reference` 保留 source socket；
- 缺 source socket 的 reference row 使用 span-aware fallback；
- `SelectableContainer` 跳过 invalid/missing source socket；
- `SelectableShop` 按 total span 居中；
- shop overflow 跳过剩余卡。

Evidence:
[`Program.cs:165-271`](../../../tests/HistoryPanelPreview.Tests/Program.cs#L165-L271).

这些测试继续作为 display socket assignment 的 source of truth。

### 新增测试

1. `BppItemBoardPreviewMapper` 传递 `DisplaySpan`。
   - 输入：large card at display socket 4。
   - 期望：mapped spec 有 `SocketId = Socket_4` 且 `DisplaySpan = 3`。

2. `BppItemBoardPreview` 保留构造时的 layout mode。
   - 可以抽一个小的 option-forwarding helper 做纯测试，避免直接检查 coroutine 内部。
   - 期望：用 `SlotGrid` 构造时，surface 收到的也是 `SlotGrid`。

3. `ItemBoardSlotGridGeometry` 数值测试。
   - row width 1000、height 200、socket 2、span 1 -> x=200、width=100。
   - socket 3、span 2 -> x=300、width=200。
   - socket 5、span 3 -> x=500、width=300。
   - socket 8、span 3 -> clamp 到 slots 8-9，width=200。

4. LivePanel row chrome 对齐测试。
   - 尽量做成纯数值测试：slot backdrop / hit / marker geometry 都来自同一 10-slot helper 或等价 contract。
   - 断言 medium card 的 hit target 和 marker 覆盖的 occupied range 与 native preview spec 相同。

5. architecture boundary。
   - `GameInterop/ItemBoardPreview` 不能 import `Game.LiveBuildPanel` 或 `Game.HistoryPanel`。
   - LivePanel 和 HistoryPanel 继续通过 `GameInterop.ItemBoardPreview` 共享 runtime/layout，不互相 import 内部类型。
     当前 architecture tests 已经覆盖这个方向。Evidence:
     [`CoreLayeringTests.cs:232-294`](../../../tests/Architecture.Tests/CoreLayeringTests.cs#L232-L294).

## 验证

本地验证：

```bash
./run.sh test
dotnet build BazaarPlusPlus.csproj
git diff --check
```

运行时验证必须通过 Steam 启动游戏：

```bash
open "steam://run/1617400"
```

手动检查：

1. Caps 打开 `LiveBuildPanel`。
2. 四行都以整行宽度作为 10 个等宽逻辑 slot。
3. shop item 按 planner 计算出的居中 display slot 展示，不按 frame width 再 packed。
4. board/stash 保留空位和真实 left socket。
5. 小/中/大卡分别在 1/2/3 slot 占用范围内居中。
6. candidate marker、click hit target 和 native card occupied range 对齐。
7. 关闭 slot 背板后，card layout、hit target、candidate marker 仍然正确。
8. HistoryPanel 的 run/battle/ghost preview 保留 socket gap 和大卡 span，且默认不显示可见 grid backdrop。

## 非目标

- 不新增游戏动作。display slot 只服务 UI 展示，不是 buy/sell/move target。
- 不重启 offscreen camera / RenderTexture preview 路线。
- 不把 native card art 非等比拉伸到任意 slot 尺寸。
- 不把 LivePanel candidate UI、slot 背板样式、marker chrome 下沉到 `GameInterop`。
- shop 行不使用 `Packed` layout；shop 居中已经是 planner 职责。
