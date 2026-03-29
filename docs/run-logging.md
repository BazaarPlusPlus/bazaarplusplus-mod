# Run Logging And History

## Scope

当前 run logging 由三部分组成：

- live run capture 到 SQLite
- 游戏内 `HistoryPanel` 浏览与管理
- `scripts/export_run_log.py` 离线导出

相关代码以 `Game/RunLogging/`、`Game/HistoryPanel/`、`Game/PvpBattles/` 为准。

## Runtime Entry

`Plugin.Awake()` 当前挂载：

- `RunStateSyncController`
- `RunLoggingController`
- `HistoryPanel`
- `HistoryCollectionsEntryBridge`

`Patches/RunLogging/RunInitializedPatch.cs` 还会监听 `NetMessageRunInitialized`，在会话创建前拿到服务端 `run_id`。

## 运行时流程

1. `RunLifecycleModule` 维护当前 run 上下文，比如 `IsInGameRun` 和 `CurrentServerRunId`。
2. `RunStateSyncController` 每 `0.25s` 发布一次 `RunLoggingSyncRequested`。
3. `RunLoggingModule` 在 live run 中读取当前游戏状态，确保 session 存在并追加事件。
4. run 退出时，模块写入 completion；若 battle replay 仍在异步落盘，会短暂延后关闭 session。

## 当前写入内容

- `runs`：run 级摘要
- `run_events`：append-only 事件流
- `run_checkpoints`：最近 checkpoint
- `run_status`：run 终态
- `pvp_battles`：本地保存的 battle manifest
- `ghost_battles`：从服务端同步回来的 ghost battle 摘要
- `run_sync_state` / `replay_sync_state`：后台上传状态

## HistoryPanel

`HistoryPanel` 是当前读侧 UI，主要能力：

- 浏览最近 runs
- 查看选中 run 关联的本地 PVP battles
- 预览保存的 player / opponent board 快照
- 在条件满足时启动本地 replay
- 同步和浏览 ghost battles
- 删除 run 及其关联 battle 记录

默认热键是 `F8`。面板内部还带有 preview tuning 的调试热键，见 `docs/reference/hotkeys-reference.md`。

## 关键文件

- `Plugin.cs`
- `Game/RunStateSyncController.cs`
- `Game/RunLifecycle/RunLifecycleModule.cs`
- `Patches/RunLogging/RunInitializedPatch.cs`
- `Game/RunLogging/RunLoggingController.cs`
- `Game/RunLogging/RunLoggingModule.cs`
- `Game/RunLogging/RunLogSessionManager.cs`
- `Game/RunLogging/RunLoggingGameDataReader.cs`
- `Game/RunLogging/Persistence/SqliteRunLogStore.cs`
- `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs`
- `Game/HistoryPanel/HistoryPanel.cs`
- `Game/HistoryPanel/HistoryPanelRepository.cs`
- `Game/HistoryPanel/GhostBattleSyncService.cs`
- `Game/HistoryPanel/HistoryPanelReplayService.cs`
- `Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs`
- `scripts/export_run_log.py`
