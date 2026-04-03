# Run Upload

## Scope

当前上传实现是一个可选的后台同步层，建立在本地 SQLite 之上。

- 本地 SQLite 仍然是 source of truth。
- 默认开启。
- 仅当 `CommunityContribution.Enabled = true` 时启用。
- 仅在玩家不处于 live run 时执行。
- 当前代码只实现单路由注册与上传，没有文档化的多区域自动路由逻辑。

## Client Flow

1. 正常 run logging 持续写入本地 SQLite。
2. `ReplicatedRunLogStore` 在完成 run 后把 `run_sync_state` 标记为 dirty。
3. `RunUploadController` 在启动延迟后扫描待上传 completed runs。
4. 客户端按需读取或创建：
   - `install-id.txt`
   - `run-upload-client.json`
   - `run-upload-rsa.json`
5. 若当前 route scope 没有 `client_id`，先调用 `POST /clients/register`。
6. 随后把 completed run snapshot 签名后上传到 `POST /runs/upload`。

`BattleUploadController` 复用同一套身份与注册信息，并从 run upload endpoint 推导 `POST /battles/upload`。

## 当前配置

`BazaarPlusPlus.cfg`

```ini
[CommunityContribution]
Enabled = true
```

## 当前实现文件

- `Game/RunLogging/Persistence/ReplicatedRunLogStore.cs`
- `Game/RunLogging/Upload/RunUploadController.cs`
- `Game/RunLogging/Upload/RunUploadService.cs`
- `Game/RunLogging/Upload/RunUploadSqliteStore.cs`
- `Game/RunLogging/Upload/RunUploadRegistrationClient.cs`
- `Game/RunLogging/Upload/RunUploadApiClient.cs`
- `Game/RunLogging/Upload/RunUploadRequestSigner.cs`
- `Game/RunLogging/Upload/RunUploadIdentityStore.cs`
- `Game/RunLogging/Upload/RunUploadKeyStore.cs`
- `Game/CombatReplay/Upload/BattleUploadController.cs`
- `Game/CombatReplay/Upload/BattleUploadService.cs`

## Notes

- 当前 `ModCFServer` 的上传/ghost 链路采用“已注册并能正确签名的客户端默认诚实”的轻量信任模型，不是强鉴权设计。
- 已知限制：
  - `POST /clients/bind` 当前信任客户端上报的 `player_account_id`，服务端不会独立证明该账号归属。
  - `POST /runs/upload` 当前会直接投影上传体里的 battle 身份字段，默认这些字段由客户端诚实提供。
  - `POST /battles/upload` 当前只校验签名和 battle/run 标识自一致性，不校验该客户端是否真的拥有该 battle；注册为 `replays` 的客户端仍可构造任意 `battle_manifest` / `opponent_account_id` 并写入 `pvp_battles`，因此 ghost 列表默认建立在“已注册客户端会诚实上报”的假设上。
  - `POST /battles/upload` 以 `battle_id` 为幂等键并允许 `ON CONFLICT` 覆盖；已知 `battle_id` 的客户端仍可能覆盖已有 battle 行与 replay object metadata，服务端当前不会阻止跨客户端改写。
- 以上风险当前按项目体量接受，优先保持实现简单；如果后续出现滥用，再考虑补更强的账号归属证明、battle 所有权校验或禁用覆盖写入。
- 旧的未来态 identity / binding / dual-backend 设计文档已移除，避免与当前实现混淆。
- 如果后续重新引入多路由或账号绑定，应以新的实现为准重新写文档，而不是恢复旧设计稿。
