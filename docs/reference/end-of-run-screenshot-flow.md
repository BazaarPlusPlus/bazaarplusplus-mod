# Screenshot Capture and Persistence

## Scope

当前截图逻辑已经不再只是“结束页自动截图”。

它现在统一覆盖三类截图：

1. `manual_f9`
2. `pvp_battle_nextday`
3. `end_of_run_auto`

目标是同时满足：

- 游戏内正常保存截图文件
- 本地 SQLite 保留稳定、可外部读取的截图元数据
- 后续可以按 `run_id` 展示整局截图
- 终局自动截图可以作为该局主图

## Capture Types

### `manual_f9`

玩家按下 `F9` 时保存一张截图。

特点：

- 全局可触发
- 会写入 SQLite
- 关联当前 `run_id`
- 不关联 `battle_id`
- 不会标记为主图

### `pvp_battle_nextday`

PVP battle 的截图不是“任意继续都截”，而是：

- 拦截 `BoardRecapReplayButtonsController.Continue()`
- 只在这次点击之后会进入 `NextDay` 时才截图

当前实现使用 `Data.NewDayTransitionController?.HasPendingDayChanged == true` 作为“这次 continue 会进入新一天”的判定信号。

特点：

- 只针对 PVP
- 只截一次
- 关联 `run_id + battle_id`
- 不会标记为主图

### `end_of_run_auto`

游戏结束界面第一次有效点击 `Continue` 时保存一张截图。

这里的“有效”指：

1. 当前不在结束页转场中
2. 本局还没有保存过结束页自动截图

特点：

- 自动触发
- 会写入 SQLite
- 关联 `run_id`
- 不关联 `battle_id`
- `is_primary = 1`

## Storage Layout

### Filesystem

截图文件继续保存到：

- 根目录：`<GameRoot>/BazaarPlusPlus/Screenshots`

路径继续按日期分层：

- `YYYY-MM-DD`

文件名现在强调“唯一性”和“来源语义”，不再只使用 `runId + 时间戳`。

当前格式示例：

- `2026-04-08/run-001-manual_f9-213015000-shotmanual001.png`
- `2026-04-08/run-001-pvp_battle_nextday-battle-001-213025000-shotbattle001.png`
- `2026-04-08/run-001-end_of_run_auto-213035000-shotprimary001.png`

说明：

- `run_id` 会先做清洗
- battle 图会把 `battle_id` 带进文件名
- `screenshot_id` 会进入文件名，保证同毫秒内也不会碰撞
- 文件名可读性是次要目标；当前优先保证唯一和稳定

### SQLite

截图元数据写入和 run log 同一个 SQLite 数据库：

- `bazaarplusplus.db`

新增表：

- `run_screenshots`

## `run_screenshots` Table

每一行表示一张截图。

核心字段：

- `screenshot_id`
  - 截图唯一 ID
- `run_id`
  - 所属 run
- `battle_id`
  - 所属 PVP battle；非 battle 图为 `NULL`
- `capture_source`
  - `manual_f9` / `pvp_battle_nextday` / `end_of_run_auto`
- `is_primary`
  - 是否为该 run 主图；目前只有终局自动图会置 `1`
- `image_relative_path`
  - 相对 `BazaarPlusPlus/Screenshots` 根目录的相对路径
- `captured_at_local`
  - 截图发生时的本地时间戳
- `captured_at_utc`
  - 同一时刻的 UTC 时间戳
- `day`
  - 截图当时的 run day
- `player_rank`
  - 截图当时读取到的 rank
- `player_rating`
  - 截图当时读取到的 rating
- `player_position`
  - 截图当时读取到的 leaderboard position
- `victories_at_capture`
  - 截图当时的胜场数

注意：

- 这里存的是 `victories_at_capture`，不是 `final_wins`
- battle 图和 `F9` 图发生时，run 可能还没结束，不能把它们误写成 final wins

## Runtime Data Sources

### `run_id`

`run_id` 的读取顺序：

1. controller 当前缓存值
2. `BppRuntimeHost.RunContext.CurrentServerRunId`

在 `RunEnded` / `RunInterrupted` 后会清空，避免 `F9` 把菜单截图串到上一局。

### `battle_id`

`battle_id` 不从 SQLite 回查。

当前来源是：

1. PVP replay artifact 创建时生成 `battle_id`
2. `CombatReplayRuntime` 立即发布 battle screenshot context event
3. `EndOfRunScreenshotController` 缓存当前 pending battle context
4. next-day 那次 battle continue 截图直接消费该 context

这样做的原因是 battle persistence 本身是异步的，截图入口不能依赖 “battle row 已经先写进 SQLite”。

### Rank / Rating / Position / Day / Wins

截图元数据读取时机统一是“截图排队当下”。

字段来源：

- `player_rank` / `player_rating`
  - `RunLoggingGameDataReader.TryGetPlayerRankSnapshot(...)`
- `player_position`
  - `BppClientCacheBridge.TryGetPlayerLeaderboardPosition(...)`
- `day`
  - `Data.Run.Day`
- `victories_at_capture`
  - `Data.Run.Victories`

补充：

- 对已有本地库，`run_screenshots.player_position` 会在 schema 初始化时自动补列
- 不要求用户删除旧的 `bazaarplusplus.db`

## Capture Flow

### Manual `F9`

1. `EndOfRunScreenshotController.Update()` 检测 `F9`
2. 调用 `ScreenshotService.CaptureCurrentFrame(...)`
3. 生成唯一 `screenshot_id`
4. 生成相对路径和绝对路径
5. 调用 `ScreenCapture.CaptureScreenshot(...)`
6. 立即把元数据写入 `run_screenshots`

### PVP Battle Next-Day Continue

1. `CombatReplayRuntime` 创建 PVP artifact 时发布 `PvpBattleScreenshotContextAvailable`
2. controller 缓存 pending `battle_id`
3. patch 拦截 `BoardRecapReplayButtonsController.Continue()`
4. 仅当：
   - 当前 battle 未截过
   - `HasPendingDayChanged == true`
   - 当前 capture 不在 in-flight
   时才启动截图协程
5. 协程在 `WaitForEndOfFrame` 后排队截图
6. 元数据写入 SQLite
7. 下一帧放行一次性 passthrough，恢复原始 continue

### End-of-Run Auto

1. patch 拦截 `EndOfRunScreenController.OnContinueClick()`
2. 只有本局第一次有效 continue 才触发
3. 协程在 `WaitForEndOfFrame` 后排队截图
4. 元数据写入 SQLite，并设置 `is_primary = 1`
5. 下一帧放行一次性 passthrough，恢复原始 continue

## Continue Interception

为避免“同一帧排队截图，但 UI 已经先切页”的问题，battle 和终局截图都采用相同模式：

1. prefix 拦截原始 continue
2. 当前帧末截图
3. 下一帧再手动调用原始 continue

当前实现还显式 suppress 了 capture in-flight 期间的后续 continue，避免多次点击穿透原始逻辑。

## External Reader Contract

外部软件如果要读取 BazaarPlusPlus 截图，不需要依赖游戏内部逻辑，只需要读取 SQLite：

### 查一局所有截图

按 `run_id` 查询 `run_screenshots`

### 查一局主图

按：

- `run_id = ?`
- `is_primary = 1`

### 查一局所有 battle 截图

按：

- `run_id = ?`
- `battle_id IS NOT NULL`

### 拼图片路径

外部软件应使用：

- `BazaarPlusPlus/Screenshots` 根目录
- 加上 `image_relative_path`

说明：

- SQLite 里存的是相对路径，不是绝对路径
- 这样外部软件和 mod 本体都能在不同安装目录下稳定工作

## Constraints

SQLite 当前保证：

- 同一 `battle_id` 最多一张 battle screenshot
- 同一 `run_id` 最多一张 primary screenshot

## Important Caveat

`ScreenCapture.CaptureScreenshot(...)` 在 Unity 里是“排队保存”，不是“同步写盘完成”。

因此：

- `run_screenshots` row 写入成功
- 不等于图片文件已经 100% 落盘成功

外部读取程序应当把 `image_relative_path` 当成“预期文件位置”，展示前最好确认文件存在。

## Key Files

- `Game/Screenshots/EndOfRunScreenshotController.cs`
- `Game/Screenshots/EndOfRunScreenshotGate.cs`
- `Game/Screenshots/ScreenshotService.cs`
- `Game/Screenshots/ScreenshotPathBuilder.cs`
- `Game/Screenshots/RunScreenshotRecord.cs`
- `Game/Screenshots/RunScreenshotMetadataReader.cs`
- `Game/Screenshots/Persistence/RunScreenshotSqliteStore.cs`
- `Patches/EndOfRun/EndOfRunScreenshotPatch.cs`
- `Patches/Combat/PvpBattleNextDayScreenshotPatch.cs`
- `Game/CombatReplay/CombatReplayRuntime.cs`
- `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs`

## Verification

当前已跑的 focused verification：

```bash
dotnet run --project tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj
dotnet run --project tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj
dotnet run --project tests/EndOfRunScreenshotGate.Tests/EndOfRunScreenshotGate.Tests.csproj
```

当前自动化覆盖：

- screenshot schema
- screenshot store 插入与唯一性约束
- screenshot path builder
- 结束页 continue gate 基本行为

未覆盖的部分：

- 游戏内 `BoardRecapReplayButtonsController.Continue()` 的真实 battle 截图效果
- `HasPendingDayChanged` 在 live 客户端里的触发时机
- 截图文件实际落盘时机与外部读取并发
