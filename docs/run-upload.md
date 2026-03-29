# Run Upload

## Scope

当前上传实现是一个可选的后台同步层，建立在本地 SQLite 之上。

- 本地 SQLite 仍然是 source of truth。
- 默认关闭。
- 仅当 `RunUpload.Enabled = true` 时启用。
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

`CombatReplayUploadController` 复用同一套身份与注册信息，并从 run upload endpoint 推导 `POST /replays/upload`。

## 当前配置

`BazaarPlusPlus.cfg`

```ini
[RunUpload]
Enabled = false
Endpoint = https://mod-api.bazaarplusplus.com/runs/upload
RegistrationEndpoint = https://mod-api.bazaarplusplus.com/clients/register
StartupDelaySeconds = 20
IntervalSeconds = 180
BatchSize = 3
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
- `Game/CombatReplay/Upload/CombatReplayUploadController.cs`
- `Game/CombatReplay/Upload/CombatReplayUploadService.cs`

## Notes

- 旧的未来态 identity / binding / dual-backend 设计文档已移除，避免与当前实现混淆。
- 如果后续重新引入多路由或账号绑定，应以新的实现为准重新写文档，而不是恢复旧设计稿。
