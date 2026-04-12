# Screenshot Capture and Persistence

## Scope

当前截图逻辑只保留一种来源：

1. `end_of_run_auto`

也就是说，Bazaar++ 现在只会在终局页面自动保存截图。

以下旧能力已移除：

- `F9` 手动截图
- 设置面板 `CAM` 按钮截图
- PVP 战斗截图

## Trigger

`EndOfRunScreenController.OnContinueClick()` 会被 patch 拦截。

只有满足以下条件时，才会触发截图：

1. 当前不在结束页转场中
2. 本局还没有完成过一次终局自动截图
3. 当前没有正在进行中的截图尝试

截图会在当前帧末尾排队，随后下一帧放行原始 `Continue`。

## Storage Layout

### Filesystem

截图文件保存到：

- `<GameRoot>/BazaarPlusPlus/Screenshots`

路径按日期分层：

- `YYYY-MM-DD`

当前文件名格式示例：

- `2026-04-08/2026-04-08_21-30-25-000_final_run-run-001.png`

### SQLite

截图元数据写入：

- `bazaarplusplus.db`

表名：

- `run_screenshots`

## `run_screenshots` Semantics

运行时当前只会写入：

- `capture_source = end_of_run_auto`
- `is_primary = 1`

表结构仍然保留了 `battle_id` 等通用字段，但终局截图不会写入 battle 维度。

## Runtime Data Sources

### `run_id`

读取顺序：

1. controller 缓存值
2. `BppRuntimeHost.RunContext.CurrentServerRunId`

### Hero / Rank / Rating / Position / Day / Wins

截图元数据在截图排队当下读取：

- `hero_name`
  - 当前 hero 名称
- `player_rank` / `player_rating`
  - `RunLoggingGameDataReader.TryGetPlayerRankSnapshot(...)`
- `player_position`
  - `BppClientCacheBridge.TryGetPlayerLeaderboardPosition(...)`
- `day`
  - `Data.Run.Day`
- `victories_at_capture`
  - `Data.Run.Victories`

## Capture Flow

1. patch 拦截 `EndOfRunScreenController.OnContinueClick()`
2. gate 判断这是不是本局第一次有效 continue
3. 进入 `WaitForEndOfFrame`
4. `ScreenshotService.CaptureCurrentFrame(...)` 写 PNG
5. `RunScreenshotSqliteStore.Save(...)` 写入元数据
6. 标记本局截图已完成
7. 下一帧放行一次性 passthrough，恢复原始 `Continue`

## UI Suppression

截图时会临时隐藏 Bazaar++ 自己的覆盖 UI，主要包括：

- Settings dock
- Combat status bar

这样终局截图不会把 Bazaar++ 浮层一起拍进去。

## External Reader Contract

外部读取方如果想拿到这局主图，只需要查询：

- `run_id = ?`
- `is_primary = 1`

图片绝对路径应通过以下方式拼接：

- `BazaarPlusPlus/Screenshots`
- 加上 `image_relative_path`
