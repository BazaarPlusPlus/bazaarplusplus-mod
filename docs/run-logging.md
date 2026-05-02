# Run Logging And History

## Scope

当前 run logging 由三部分组成：

- live run capture 到 SQLite
- 游戏内 `HistoryPanel` 浏览与管理
- run-bundle 上传队列状态

相关代码以 `Game/RunLogging/`、`Game/HistoryPanel/`、`Game/PvpBattles/` 为准。

## Runtime Entry

`Plugin.Awake()` 当前通过 `BppComposition` 启动 `RunLifecycleModule`，并挂载：

- `RunLoggingController`
- `RunUploadController`
- `HistoryPanel`

`Patches/RunLogging/RunInitializedPatch.cs` 监听 `NetMessageRunInitialized`，在会话创建前拿到服务端 `run_id`。

## 运行时流程

1. `RunLifecycleModule` 维护当前 run 上下文，比如 `IsInGameRun` 和 `CurrentServerRunId`。
2. `RunLoggingModule` 订阅 `RunLifecycleChanged`、`RunInitializedObserved`、`PvpBattleRecorded` 和 `CombatReplayPersistenceDrained`。
3. 进入 live run 后，`RunLoggingModule` 从当前游戏状态确保 session 存在并追加关键事件。
4. PVP battle 落盘后，模块把 battle 关联到当前 run，并追加 capture event。
5. run 退出时，模块写入 completion；若 battle replay 仍在异步落盘，会短暂延后关闭 session。

## 当前写入内容

- `runs`：run 级摘要、checkpoint 和终态字段
- `run_events`：append-only 事件流
- `battles`：本地 PVP battle 和 ghost battle 的统一投影，包含 ghost UI 使用的 `is_bundle_final_battle`
- `battle_snapshots`：battle 对应的 board snapshot
- `sync_cursors`：ghost sync checkpoint
- `run_sync_state`：后台 run-bundle 上传状态
- replay payload 文件：`<GameRoot>/BazaarPlusPlus/CombatReplays`

## HistoryPanel

`HistoryPanel` 是当前读侧 UI，主要能力：

- 按时间倒序浏览 runs
- 查看选中 run 关联的本地 PVP battles
- 预览保存的 player / opponent board 快照
- 在条件满足时启动本地 replay
- 同步和浏览 ghost battles
- 选中的 ghost battle 如果是上传 bundle 的最后一战，且本地视角为胜利，会显示“对手出局”提示
- 删除 run 及其关联 battle 记录

大厅内通过 Bazaar++ settings dock 的 `Game History` 入口打开，也可用 `F8` 切换。面板内部还带有 preview tuning 的调试热键，见 `docs/reference/hotkeys-reference.md`。

## 关键文件

- `Plugin.cs`
- `BppComposition.cs`
- `Game/RunLifecycle/RunLifecycleModule.cs`
- `Patches/RunLogging/RunInitializedPatch.cs`
- `Game/RunLogging/RunLoggingController.cs`
- `Game/RunLogging/RunLoggingModule.cs`
- `Game/RunLogging/RunLogSessionManager.cs`
- `Game/RunLogging/RunLoggingGameDataReader.cs`
- `Game/RunLogging/Persistence/SqliteRunLogStore.cs`
- `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs`
- `Game/RunLogging/Upload/RunUploadController.cs`
- `Game/HistoryPanel/HistoryPanel.cs`
- `Game/HistoryPanel/HistoryPanelRepository.cs`
- `Game/HistoryPanel/Ghost/GhostBattleSyncService.cs`
- `Game/HistoryPanel/Ghost/GhostBattleLocalProjector.cs`
- `Game/HistoryPanel/HistoryPanelReplayService.cs`
- `Game/HistoryPanel/HistoryPanelFormatter.cs`
- `Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs`
