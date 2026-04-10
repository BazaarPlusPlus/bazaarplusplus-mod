# Combat Replay Recording

## Scope

当前实现会把完成的 `PVPCombat` 保存为与游戏原生 replay 流程兼容的三消息 bundle：

1. opening `NetMessageGameSim`
2. `NetMessageCombatSim`
3. closing `NetMessageGameSim`

## Storage

- payload 文件目录：`<GameRoot>/BazaarPlusPlus/CombatReplays`
- payload 文件格式：`<battle_id>.payload.mpack.gz`
- battle metadata：SQLite `pvp_battles`

`pvp_battles` 保存 battle manifest，包含 battle identity、player/opponent identity、结果，以及 history preview / replay bootstrap 需要的 board snapshot。

## Capture Flow

1. `Patches/Combat/CombatReplayCapturePatch.cs` 监听相关 net messages。
2. `Game/CombatReplay/CombatReplayModule.cs` 转发消息给 `CombatReplayRuntime`。
3. `Game/CombatReplay/CombatReplayCaptureService.cs` 组装三消息 bundle，并抓取 player / opponent board snapshot。
4. `Game/CombatReplay/CombatReplayRuntime.cs` 通过 `CombatReplayPersistenceQueue` 异步持久化 payload 与 manifest。
5. 完成后发布 `PvpBattleRecorded`，供 run logging 等模块消费。

## Replay Entry Points

- `HistoryPanel`：本地 battle payload 存在且当前允许 bootstrap 时可回放
- `DebugPanel -> Replays`：debug build 下的调试入口
- ghost battle：若服务端声明 replay 可用，可先下载 payload，再走导入回放

## 关键文件

- `Patches/Combat/CombatReplayCapturePatch.cs`
- `Game/CombatReplay/CombatReplayRuntime.cs`
- `Game/CombatReplay/CombatReplayCaptureService.cs`
- `Game/CombatReplay/CombatReplayPersistenceQueue.cs`
- `Game/CombatReplay/CombatReplayPayloadStore.cs`
- `Game/CombatReplay/CombatReplayLoader.cs`
- `Game/HistoryPanel/HistoryPanelReplayService.cs`
