# Run Upload

## Scope

当前上传实现是一个可选的后台同步层，建立在本地 SQLite、combat replay payload 和 installer 写入的 V3 identity 文件之上。

- 本地 SQLite 仍然是 source of truth。
- 默认开启。
- 仅当 `CommunityContribution.Enabled = true` 时启用。
- 仅在玩家不处于 live run 时执行。
- 上传协议已经切到 V3 installation-signed `run-bundle`，不再使用旧 `client_id / bind-player` 链路。

## Client Flow

1. 正常 run logging 持续写入本地 SQLite。
2. `ReplicatedRunLogStore` 把 `run_sync_state` 标记为 dirty。
3. combat replay 持久化完成后把关联 battle 的 `replay_dirty` 标记为 dirty。
4. `RunUploadController` 在启动延迟后扫描待上传 completed runs。
5. 客户端读取 installer 写入的：
   - `installation.bpp`
   - `installation.key`
6. `RunBundleUploadStore` 组装 run projection、battle projections 和 replay artifact。
7. `RunBundleUploadService` 对请求进行 installation 签名，并上传到 `POST /run-bundles`。
8. 上传成功后清除 run 和关联 replay 的 dirty 标记。

## 当前配置

`BazaarPlusPlus.cfg`

```ini
[CommunityContribution]
Enabled = true
```

## 当前实现文件

- `Game/Identity/InstallationRecordStore.cs`
- `Game/Online/InstallationRequestSigner.cs`
- `Game/Online/V3Routes.cs`
- `Game/RunLogging/Persistence/ReplicatedRunLogStore.cs`
- `Game/RunLogging/Upload/RunSyncStateSqliteStore.cs`
- `Game/RunLogging/Upload/RunBundleUploadStore.cs`
- `Game/RunLogging/Upload/RunBundleUploadService.cs`
- `Game/RunLogging/Upload/RunUploadController.cs`
- `Game/CombatReplay/Upload/BattleReplaySyncStateStore.cs`

## Notes

- 当前 `ModCFServerV3` 以 `installation_id + RSA` 作为 mod 侧身份，服务端不再维护旧 `client_id` 注册和 `bind-player` 状态。
- ghost 查询与 replay-link 现在是：
  - `GET /ghost-battles`
  - `POST /ghost-battles/:battleId/replay-link`
  - `GET /replays/:token`
- `POST /run-bundles` 会同时携带 projection 和 artifact；首版接受轻量一致性策略，不要求服务端完整解包 replay artifact。
- battle gate 仍然允许“artifact 已收但 battle 不可查询”的设计，这是当前接受的产品取舍，不是实现遗漏。
