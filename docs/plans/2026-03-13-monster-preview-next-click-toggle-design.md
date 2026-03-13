# Monster Preview Next Click Toggle Design

## Goal

把当前 `monsterPreview` 的交互从“右键再次 toggle 关闭”扩展为：

- preview 打开后，下一次鼠标左键或右键都关闭 preview
- 这次点击被 BazaarPlusPlus 吞掉，不继续原本点击行为
- 关闭语义与当前右键 toggle 保持一致

## Current Behavior

- 右键怪物卡会通过 `CardTooltipController.LockTooltipToggle()` 补丁进入 `MonsterLockShowcaseRuntime`
- 如果 preview 未激活，则构建 request 并打开 preview
- 如果 preview 已激活，则同一路径调用 `HideOverlay(...)` 关闭 preview
- 左键没有同等的关闭入口

## Proposed Behavior

新增一个“一次性点击关闭”状态：

- preview 成功打开时，将状态置为激活
- 当下一次任意左键或右键发生时：
  - 若 preview 仍处于激活状态，则调用现有关闭链路
  - 清掉“一次性点击关闭”状态
  - 优先把这次点击用于关闭 preview，而不是继续保留 preview
- 如果 preview 已关闭，则该状态失效，不再影响后续点击

## Design

### Runtime

在 `MonsterLockShowcaseRuntime` 中新增一层纯逻辑 gate：

- preview 打开时标记“等待下一次点击关闭”
- 在 `Update()` 中全局监听鼠标下一次左键或右键
- 关闭 preview 后清理 `_lockedCard` 和一次性 gate

这样可以让行为覆盖整个运行时，而不是只覆盖某张卡的点击链路。

### Patch Entry Points

保留右键 `LockTooltipToggle()` 入口作为兜底：

- `CardTooltipController.LockTooltipToggle()`：在某些同帧顺序下仍可优先关闭 preview

不再依赖 `CardController.ProceedClick(...)` 去覆盖左键，因为那样只能拦截卡牌点击，不是全局行为。

### Testing

优先覆盖纯逻辑，不依赖 Unity/Game runtime：

- preview 打开后，下一次左键应触发关闭 gate
- preview 打开后，下一次右键应触发关闭 gate
- gate 只消费一次；关闭后不再继续消费点击
- preview 未打开时，左键不会被错误消费

## Non-Goals

- 不做“点击任意 UI 空白区域也关闭”的全局输入扫描
- 不改变 preview 的数据构建、渲染、anchor 或异步 rebuild 逻辑
- 不把当前交互改成“点击另一个怪物直接切换到新 preview”
