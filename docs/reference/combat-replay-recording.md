# Combat Replay Recording

## Scope

当前实现会把完成的 `PVPCombat` 保存为与游戏原生 replay 流程兼容的三消息 bundle：

1. opening `NetMessageGameSim`
2. `NetMessageCombatSim`
3. closing `NetMessageGameSim`

## Storage

- payload 文件目录：`<GameRoot>/BazaarPlusPlus/CombatReplays`
- payload 文件格式：`<battle_id>.payload.mpack.gz`
- battle metadata：SQLite `battles` / `battle_snapshots`

`battles` 保存 battle manifest，`battle_snapshots` 保存 history preview / replay bootstrap 需要的 board snapshot。

## Capture Flow

1. `Patches/Combat/CombatReplayCapturePatch.cs` 监听相关 net messages。
2. `Game/CombatReplay/CombatReplayModule.cs` 转发消息给 `CombatReplayRuntime`。
3. `Game/CombatReplay/CombatReplayCaptureService.cs` 组装三消息 bundle，并抓取 player / opponent board snapshot。
4. `Game/CombatReplay/CombatReplayRuntime.cs` 通过 `CombatReplayPersistenceQueue` 异步持久化 payload 与 manifest。
5. 完成后发布 `PvpBattleRecorded`，供 run logging 等模块消费。

## Replay Entry Points

- `HistoryPanel`：本地 battle payload 存在且当前允许 bootstrap 时可回放
- ghost battle：若服务端声明 replay 可用，可先下载 payload，再走导入回放

## Playback Events

`CombatReplayRuntime` 在 saved replay 真正开始播放前后会发布两个事件供独立 feature 订阅，不依赖 capture path：

- `CombatReplayPlaybackStarting`（在 `replayState.Replay()` 之前）：携带 `BattleId`、`Manifest`、`Source`（`LocalSaved` / `ImportedGhost`）。
- `CombatReplayPlaybackEnded`（离开 `ReplayState` 时，或 start 失败的 catch 分支）：携带 `BattleId`、`Reason`、`Failed`。

视频录制 feature 就是这两个事件的订阅者。

## Optional：Video Recording

可选的 MP4 录制 feature 由 `CombatReplayVideoRecorder`（`Game/CombatReplay/Video/`）实现，默认关闭（`CombatReplayVideo / Enabled = false`）。设计与运维细节见 [combat-replay-video-recording.md](../combat-replay-video-recording.md)；SQLite 元数据见 [sqlite-schema-reference.md](sqlite-schema-reference.md) 的 `combat_replay_videos` 章节。

## 关键文件

- `Patches/Combat/CombatReplayCapturePatch.cs`
- `Game/CombatReplay/CombatReplayRuntime.cs`
- `Game/CombatReplay/CombatReplayRuntime.Bootstrap.cs`
- `Game/CombatReplay/CombatReplayCaptureService.cs`
- `Game/CombatReplay/CombatReplayPersistenceQueue.cs`
- `Game/CombatReplay/CombatReplayPayloadStore.cs`
- `Game/CombatReplay/CombatReplayLoader.cs`
- `Game/CombatReplay/CombatReplayPlaybackStarting.cs`
- `Game/CombatReplay/CombatReplayPlaybackEnded.cs`
- `Game/HistoryPanel/HistoryPanelReplayService.cs`
