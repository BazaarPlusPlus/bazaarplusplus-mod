# Run Upload

## Scope

当前上传实现是后台同步层，建立在本地 SQLite、combat replay payload 和共享身份 JSON 文件之上。

- 本地 SQLite 仍然是 run / battle 的 source of truth。
- 仅在玩家不处于 live run 时执行。
- 上传协议是未认证的 V3 `run-bundle` 上传；服务端只把已注册玩家或上传者本人作为可投影 opponent，不使用 installation 签名。
- 读类端点使用 installer 写入 `auth.v1.json` 的 bearer token。

## Client Flow

1. 正常 run logging 持续写入本地 SQLite。
2. `ReplicatedRunLogStore` 把 `run_sync_state` 标记为 dirty。
3. combat replay 持久化完成后把关联 battle 的 `replay_dirty` 标记为 dirty。
4. `RunUploadController` 在启动延迟后，或 run 退出 / replay 落盘完成后，扫描待上传 completed runs。
5. `RunBundleUploadStore` 组装 run projection、battle projections 和 replay artifact。
6. `RunBundleUploadService` 直接 `POST /run-bundles`，不附加 Authorization 头。
7. 上传成功后清除 run 和关联 replay 的 dirty 标记。

## 当前实现文件

- `Game/Identity/AuthStore.cs`
- `Game/Identity/PlayerObservationStore.cs`
- `Game/Identity/IdentityJsonFileStore.cs`
- `Game/Online/V3Routes.cs`
- `Game/Online/ModOnlineClient.cs`
- `Game/Online/RunBundleClient.cs`
- `Game/RunLogging/Persistence/ReplicatedRunLogStore.cs`
- `Game/RunLogging/Upload/RunSyncStateSqliteStore.cs`
- `Game/RunLogging/Upload/RunBundleUploadStore.cs`
- `Game/RunLogging/Upload/RunBundleUploadService.cs`
- `Game/RunLogging/Upload/RunUploadController.cs`
- `Game/CombatReplay/Upload/BattleReplaySyncStateStore.cs`

## Notes

- 身份目录是 `<GameRoot>/BazaarPlusPlus/Identity/`。
- 当前身份文件是 `auth.v1.json` 和 `observation.v1.json`；旧 `identity.db`、`identity.db-wal`、`identity.db-shm` 会被当前 JSON store 清理。
- ghost 查询与 replay-link 现在是：
  - `GET /ghost-battles`
  - `POST /ghost-battles/:battleId/replay-link`
  - `GET /replays/:token`
- `POST /run-bundles` 会同时携带 projection 和 artifact；服务端不要求完整解包 artifact 后再接受投影。
- battle gate 仍然允许“artifact 已收但 battle 不可查询”的设计，这是当前接受的产品取舍，不是实现遗漏。
