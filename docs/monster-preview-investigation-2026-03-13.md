# Monster Preview Investigation (2026-03-13)

## Summary

本次排查聚焦于“右键 Monster 后偶发不显示”的问题。

已经确认有两类问题：

1. `visible` 逻辑状态和底层 render target 可见状态可能失同步
2. `MonsterPreviewBoard` 的异步 `RebuildAsync` 存在并发竞态，旧任务可能在新状态上继续写入

其中第 1 类问题已经修复；第 2 类问题目前仍是潜在风险，且更可能解释“偶发不显示 / 偶发空白 / 状态不稳定”。

## Runtime Path

当前右键 Monster 的主链路不是旧的 `EncounterTooltipPreviewBridge`，而是：

`CardTooltipController.LockTooltipToggle`
-> `MonsterLockShowcaseRuntime.HandleLockToggle(...)`
-> `MonsterPreviewController.ShowRequest(...)`
-> `MonsterPreviewOverlayCoordinator.ShowRequest(...)`
-> `PreviewBoardSession.Tick()`
-> `MonsterPreviewBoardRenderTarget.Render(...)`
-> `MonsterPreviewBoard.RebuildAsync(...)`

相关文件：

- `Game/MonsterPreview/MonsterLockShowcaseRuntime.cs`
- `Patches/Showcase/ShowcaseTooltipPatches.cs`
- `Game/MonsterPreview/MonsterPreviewController.cs`
- `Game/MonsterPreview/Architecture/PreviewBoardSession.cs`
- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs`
- `Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs`

## Confirmed Issue 1: Visible State Could Desync

### Symptom

在同一份 `visible=true` 的 request 仍然成立时，如果底层 render target 已经被隐藏，session 可能不会再把它恢复为 visible。

### Root Cause

`PreviewBoardSession.Tick()` 之前只在真正发生 `Render()` 时才把显示状态传递到底层 target。

如果：

- request 仍然是同一份 visible request
- signature / presentation / pose 都没变
- session 认为不需要 render

那么底层 target 即使已被隐藏，也不会被重新显式设为 visible。

### Fix Applied

已在 `PreviewBoardSession.Tick()` 中增加：

- 每次 tick 都先根据 request 的 `Presentation.Visible` 调用一次 `_renderTarget.SetVisible(...)`
- 当 request 为 hidden 时直接返回，不进入后续 render 分支

这样即使没有发生新的 `Render()`，visible 状态也会正确传播到底层 target。

### Verification

新增回归测试：

- `tests/BazaarPlusPlus.Tests/MonsterPreview/PreviewBoardSessionTests.cs`

验证场景：

- 第一次 visible request 正常显示
- render target 被外部设为 hidden
- 再次 `Show(request)` + `Tick()`
- 断言 target 能恢复为 visible

## Confirmed Issue 2: Asynchronous Rebuild Has Race Conditions

### Symptom Pattern

虽然还没有单独做出完整自动化复现，但从代码结构上看，`MonsterPreviewBoard` 的异步重建存在明确竞态条件，足以造成：

- 偶发不显示
- 偶发空白
- 一次 hide/show 后内容被旧任务冲掉
- 多次右键切换时展示结果不稳定

### Evidence

在 `MonsterPreviewBoardRenderTarget.Render(...)` 中：

```csharp
_ = _board.RebuildAsync(
    renderModel.Data?.ItemCards ?? new List<PreviewCardSpec>(),
    renderModel.Data?.SkillCards ?? new List<PreviewCardSpec>(),
    () => false
);
```

这里的 `isCancelled` 永远返回 `false`，意味着旧的 rebuild 永远不会失效。

而 `MonsterPreviewBoard.RebuildAsync(...)` 会：

1. 先调用 `Clear()`
2. `await` 异步创建 item cards
3. 再 `await` 异步创建 skill cards
4. 在整个过程中持续写共享列表和对象树

由于这些操作都发生在同一个 `MonsterPreviewBoard` 实例上，多个未取消的 rebuild 会互相踩状态。

### Why This Is Dangerous

假设出现以下序列：

1. 第 1 次 render 启动 rebuild A
2. rebuild A 尚未完成时，用户再次右键或状态切换
3. 第 2 次 render 启动 rebuild B
4. hide 或 clear 发生
5. rebuild A 在稍后继续执行，并对当前 board 调用 `Clear()` / 添加卡片 / 调整列表

结果是旧任务会在新状态上继续写入，造成状态回退或空白。

### Shared Mutable State Affected

`MonsterPreviewBoard` 中以下成员都可能被并发 rebuild 交叉修改：

- `_cards`
- `_cardAnchors`
- `_cardSizes`
- `_cardCenterMarkers`
- `_skillCards`

以及整个 `_boardRoot` 下的对象树。

## Recommended Fix For Issue 2

推荐把取消机制做进 `MonsterPreviewBoardRenderTarget`，而不是继续依赖上层状态兜底。

### Suggested Approach

1. 在 `MonsterPreviewBoardRenderTarget` 中维护一个 render generation / version
2. 每次 `Render()` 时递增 generation，并捕获当前 generation
3. 将 `isCancelled` 改为：
   - 当前 generation 已过期，或
   - 当前 target 已被隐藏
4. `SetVisible(false)` 时也推进 generation，使所有旧 rebuild 自动过期
5. `MonsterPreviewBoard.RebuildAsync(...)` 继续沿用现有 `isCancelled()` 检查点，但让它真正能取消旧任务

### Why This Approach

- 修复点集中在 render target 层，不需要把竞态逻辑散落到 controller / session / board 多层
- 能同时处理 repeated render、hide/show、board recreation 等场景
- 与当前 `RebuildAsync(Func<bool> isCancelled)` 的接口天然匹配

## Notes

- 旧的 `EncounterTooltipPreviewBridge` 当前没有在 `Plugin` 中挂载，不是这次右键 Monster 主链路的一部分
- 当前主链路是 showcase runtime，而不是 tooltip bridge
- 因此后续修复应优先落在 showcase runtime 之后的 preview session / render target / board 层
