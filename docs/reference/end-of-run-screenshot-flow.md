# End-of-Run Screenshot Flow

## Scope

当前实现会在游戏结束界面第一次有效点击 `Continue` 时保存一张截图。

这里的“有效”指：

1. 当前不在结束界面转场中
2. 本局还没有保存过结束界面截图

截图目标是“用户点击前看到的当前结束页”，而不是 `RunEnded` 时的最后战斗帧。

## Storage

- 根目录：`<GameRoot>/BazaarPlusPlus/Screenshots`
- 日期分级：`YYYY-MM-DD`
- 文件名：`<run_id_or_anonymous>-HHmmss.png`
- 时间语义：本地时区时间

示例：

- `BazaarPlusPlus/Screenshots/2026-04-07/run-42final-213015.png`
- `BazaarPlusPlus/Screenshots/2026-04-07/anonymous-090504.png`

`runId` 优先使用本局缓存值；如果截图时无法取得，则回退到 `anonymous`。

## Capture Flow

1. `Plugin.AttachRuntimeComponents()` 挂载 `EndOfRunScreenshotController`。
2. `RunInitializedObserved` 到达时，controller 缓存本局 `runId`。
3. Harmony patch 拦截 `EndOfRunScreenController.OnContinueClick()`。
4. 如果当前调用是 controller 放行的 passthrough，直接执行原始继续逻辑。
5. 如果当前点击处于结束页转场中，不触发截图，也不拦截原始逻辑。
6. 如果当前是本局第一次有效点击，patch 拦截原始逻辑并启动截图协程。
7. 协程在 `WaitForEndOfFrame` 后调用 `ScreenCapture.CaptureScreenshot(...)`。
8. 协程下一帧设置一次性 passthrough，再调用原始 `OnContinueClick()`，恢复游戏原生继续逻辑。

这样做的目的是避免“同一帧先排队截图、再立刻切下一页”导致截到错误页面。

## Components

### `Game/Screenshots/EndOfRunScreenshotGate.cs`

纯逻辑 gate，负责：

- 判定本局第一次有效点击是否应触发截图
- 管理一次性 passthrough
- 在新 run 开始时重置状态

### `Game/Screenshots/ScreenshotPathBuilder.cs`

纯路径构造逻辑，负责：

- 日期目录分层
- `runId` 清洗
- `anonymous` fallback
- 本地时间 `HHmmss` 文件名

### `Game/Screenshots/ScreenshotService.cs`

截图落盘逻辑，负责：

- 组合相对路径与绝对路径
- 创建目录
- 调用 `ScreenCapture.CaptureScreenshot(...)`
- 记录日志

### `Game/Screenshots/EndOfRunScreenshotController.cs`

Unity 生命周期协调器，负责：

- 初始化截图 service
- 订阅 `RunInitializedObserved` 缓存 `runId`
- 监听 `Events.RunStarted` 重置 gate
- 在需要时启动“截图后继续”的 coroutine

### `Patches/EndOfRun/EndOfRunScreenshotPatch.cs`

Harmony 入口，负责：

- 拦截结束界面 `OnContinueClick()`
- 查询 `_transitionCount`
- 决定这次点击是 passthrough、正常放行，还是拦截后启动截图协程

## Paths and Runtime Wiring

截图目录路径通过 `IPathService` / `BppPathService` 暴露：

- `Core/Paths/IPathService.cs`
- `Core/Paths/BppPathService.cs`

新增路径：

- `BepInEx.Paths.GameRootPath/BazaarPlusPlus/Screenshots`

## Key Files

- `Plugin.cs`
- `Core/Paths/IPathService.cs`
- `Core/Paths/BppPathService.cs`
- `Game/Screenshots/EndOfRunScreenshotGate.cs`
- `Game/Screenshots/ScreenshotPathBuilder.cs`
- `Game/Screenshots/ScreenshotService.cs`
- `Game/Screenshots/EndOfRunScreenshotController.cs`
- `Patches/EndOfRun/EndOfRunScreenshotPatch.cs`
- `tests/EndOfRunScreenshotGate.Tests/Program.cs`

## Verification

本次实现已执行：

```bash
dotnet run --project tests/EndOfRunScreenshotGate.Tests/EndOfRunScreenshotGate.Tests.csproj
dotnet build BazaarPlusPlus.csproj -c Release
```

当前自动化验证覆盖：

- 首次点击 gate
- gate reset
- passthrough 一次性消费
- 路径分层与 `runId` / `anonymous` 命名规则

未覆盖的部分：

- 游戏内实际结束界面首屏是否稳定截到点击前画面
- 不同结束页切换动画下的最终截图内容

## Review Focus

建议 review 时重点看这几项：

1. `OnContinueClick()` 被拦截后，下一帧恢复原调用是否会和游戏内部状态机冲突
2. `WaitForEndOfFrame` 是否足以保证截图拿到点击前页面，而不是动画中的中间态
3. `runId` 缓存策略是否还需要额外兜底来源
4. `ScreenCapture.CaptureScreenshot(...)` 在目标平台上的实际保存时机是否符合预期
