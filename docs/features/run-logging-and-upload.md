# Run Logging, History & Upload

一个连续的故事：活跃对局写入本地 SQLite → 游戏内 `HistoryPanel` 浏览管理 → 非 live run 时后台把 run-bundle 上传到 `bazaarplusplus-server`，并同步 ghost battles。本地 SQLite 始终是 run / battle 的 source of truth。

SQLite 列定义统一见 [sqlite-schema-reference.md](../reference/sqlite-schema-reference.md)；HistoryPanel 的布局 / 预览 / 行级视觉见 [history-panel.md](history-panel.md)；ghost battle 的视角翻转数据流见 [ghost-battle-data-flow.md](ghost-battle-data-flow.md)。

## Runtime Entry

`Plugin.Awake()` 经 `BppComposition` 启动 `RunLifecycleModule`，并挂载 `RunLoggingController`、`RunUploadController` 与 `HistoryPanel`。`Patches/RunLogging/RunInitializedPatch.cs` 监听 `NetMessageRunInitialized`，在会话创建前拿到服务端 `run_id`。

## Capture Flow

1. `RunLifecycleModule` 维护当前 run 上下文（`IsInGameRun`、`CurrentServerRunId`）。
2. `RunLoggingModule` 订阅 `RunLifecycleChanged`、`RunInitializedObserved`、`PvpBattleRecorded`、`CombatReplayPersistenceDrained`。
3. 进入 live run 后确保 session 存在并追加关键事件。
4. PVP battle 落盘后把 battle 关联到当前 run 并追加 capture event。
5. run 退出时写入 completion；若 battle replay 仍在异步落盘，会短暂延后关闭 session。

## 写入的表

`runs`、`run_events`、`battles`、`battle_snapshots`、`sync_cursors`、`run_sync_state`，外加 `<GameRoot>/BazaarPlusPlusV4/CombatReplays/` 下的 replay payload 文件（见 [combat-replay.md](combat-replay.md)）。各表列定义见 schema 参考，本文不重复。

`battles.is_bundle_final_battle` 是**本地列名**，投影到服务端 wire 字段 `is_final_battle`（V4 去掉了 `is_bundle_` 冗余前缀）；两者在各自层都正确，详见 schema 参考。HistoryPanel 用它在 ghost 视角提示「这场后对手出局」。

## Background Upload & Ghost Battles

仅在玩家**不处于 live run** 时执行。

1. 正常 run logging 持续写入本地 SQLite。
2. `ReplicatedRunLogStore` 把 `run_sync_state` 标记 dirty；combat replay 落盘后把关联 battle 的 `replay_dirty` 标记 dirty。
3. `RunUploadController` 在启动延迟后、或 run 退出 / replay 落盘完成后扫描待上传的 completed runs。
4. `RunBundleUploadStore` 组装 run projection、battle projections 与 gzip MessagePack replay artifact。
5. `RunBundleUploadService` 编排上传，经 `RunBundleClient` 执行 `POST /run-bundles`（不附鉴权头）。
6. 服务端把 bundle 中最后一场 battle 投影时标记 `is_final_battle`（sticky：一旦 1 永远 1，乱序重传不会翻回）。
7. 上传成功后清除 run 与关联 replay 的 dirty 标记。

ghost 同步与 replay 下载（V4 wire，服务端在独立仓库 `bazaarplusplus-server`，部署 `mod-api-v4.bazaarplusplus.com`）：

- `GET /ghost-battles?player_account_id=…` 查询 against-me 列表。
- `POST /ghost-battles/:battleId/replay-link` 按需签发 5 分钟有效的 R2 预签 URL。

### 信任模型与安全限制

- 服务端对 mod 侧端点**全部不鉴权**；身份只来自 `POST /run-bundles` body 与 `GET /ghost-battles` query 里的 `player_account_id`。
- 服务端全量写入有效的 battle projections；`GET /ghost-battles` 再按 `opponent_account_id` 查询 against-me 列表。
- `player_account_id` 必填——V3 时代的 `"anonymous-player"` sentinel 已删除；mod 在没拿到本机 account id 时**直接跳过上传**，不再发占位符。
- replay artifact 和 battle projections 在同一个 D1 batch 后对外可查询；若 D1 batch 失败，服务端会尽力清理已写入的 R2 artifact。

## 关键文件

- `Game/RunLifecycle/RunLifecycleModule.cs`
- `Patches/RunLogging/RunInitializedPatch.cs`
- `Game/RunLogging/RunLoggingController.cs`、`RunLoggingModule.cs`、`RunLogSessionManager.cs`、`RunLoggingGameDataReader.cs`
- `Storage/RunLog/RunLogStore.cs`、`RunLogSchema.cs`、`Replication/ReplicatedRunLogStore.cs`
- `Game/RunLogging/Upload/RunUploadController.cs`、`RunBundleUploadStore.cs`、`RunBundleUploadService.cs`
- `Storage/Upload/RunSyncStateStore.cs`、`BattleReplaySyncStateStore.cs`
- `ModApi/Clients/ModOnlineClient.cs`、`RunBundleClient.cs`、`GhostBattleClient.cs`
- `Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs`
- `Game/HistoryPanel/HistoryPanel.cs`、`Storage/HistoryPanelRepository.cs`、`Ghost/GhostBattleSyncService.cs`、`Ghost/GhostBattleLocalProjector.cs`、`HistoryPanelReplayService.cs`、`HistoryPanelFormatter.cs`
