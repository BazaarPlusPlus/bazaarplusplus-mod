# BazaarPlusPlus Mod V5 数据管线改造设计

状态：待确认（确认后进入实现）

日期：2026-08-03

范围：mod 侧 V5 Bundle 构造、seal、上传、Ghost Battle 消费、Screenshot 打包，以及被替换的 V4 数据管线删除。

引用约定：

- `S:` = server 仓库 `/Users/yxinyu/codes/workspaces/bpp/bazaarplusplus-server-v5`
- `M:` = mod 仓库 `/Users/yxinyu/codes/workspaces/bpp/bazaarplusplus-mod`
- server 代码即 wire 契约。本文与 server 行为冲突时，以 `S:src/bundle/open.ts`、`S:src/modules/*` 和 `S:src/http/*` 为准。

---

## 0. 结论与不可变决策

V5 是独立发布，不兼容、不迁移、不双写 V4。最终实现只有一条数据路径：

```text
游戏事实采集
  → completed Run + 冻结的 player_account_id / Screenshot 策略
  → bundle_seal_jobs 等待输入收敛
  → 一次 seal 出不可变 .bundle
  → bundle_outbox
  → POST /bundles 幂等上传
  → server D1/R2 / Ghost projection / BazaarDB delivery
```

核心决策如下：

1. V5 数据根固定为 `<GameRoot>/BazaarPlusPlusV5/`；V4 目录不读、不删。
2. `BundleV5Codec` 是纯 wire 深模块，调用方只提交已归一化的 manifest 输入与 segment bytes；前缀、确定性 JSON、offset、digest、大小和 schema 自检全部收在模块内。
3. `BundleSealCoordinator` 是 seal 深模块。它不依赖不存在的完成事件，不在 Unity 主线程或 run lifecycle 锁内做 SQLite、gzip、图像编码或文件 I/O。
4. Run 激活与完成时把 `player_account_id` 和“本 Run 是否请求 Screenshot”落盘；历史 Run 不用当前登录账号猜身份。
5. Screenshot 开启时，seal 等待 Screenshot 成功、明确失败或输入截止时间；失败或超时一律退化为 Run-only Bundle，不阻断 Bundle。
6. 只有最终 payload 内同时保有完整 snapshots 与三段 replay 的 Battle 才可进入 manifest projection；因此任何 server 返回的 Ghost projection 都应可回放。
7. `.bundle` 文件 seal 后永不修改。上传只从文件流式读取；重试永远发送相同字节。
8. `bundle_outbox` 不外键引用 `runs`，用户删除本地 Run 不会阻断，上传审计也不会丢失。
9. V4 run/Screenshot 上传实现、dirty 状态、multipart wire、独立 Screenshot 通道在本次改造中整体删除；通用 pump 仅保留一个 `Bundle` feed。
10. Ghost 下载的是完整 Bundle；客户端先验证 Bundle，再解压 Run payload，再抽取目标 Battle。

非目标：

- 不调用 analyzer 专用 `GET /bundles` 或 BazaarDB 专用 delivery 路由（`S:docs/api-reference.md:7-14`）。
- 不改 PvP、Run、Screenshot 的游戏事实探测规则；只增加 V5 所需的身份/策略冻结、seal 唤醒和消费适配。
- 不把 V4 行为当作 V5 契约；保留代码仅限经核实与 wire 无关的采集、回放、UI 和通用调度机制。

---

## 1. V5 server 契约摘要

### 1.1 Bundle 二进制格式

```text
[16-byte prefix][manifest UTF-8 JSON][Run segment][optional Screenshot segment]
```

| 项 | 硬约束 | 证据 |
|---|---|---|
| prefix 0..7 | ASCII `BPPBNDL5` | `S:src/bundle/prefix.ts:7,20-28` |
| prefix 8..11 | u32 大端 version `5` | `S:src/bundle/prefix.ts:31-40` |
| prefix 12..15 | u32 大端 manifest UTF-8 长度，`1..2_097_152` | `S:src/bundle/prefix.ts:42-45` |
| segment 定界 | Run offset=0；Screenshot offset=Run length；无 padding、gap 或未声明尾字节 | `S:src/bundle/open.ts:291-296,351-359,486-491` |
| manifest | fatal UTF-8；嵌套深度 ≤64；根为 object；未知字段忽略；不要求 canonical JSON | `S:src/bundle/open.ts:236-263`；`S:contracts/v5/bundle-v5.md:15` |
| Run | 非空；`application/x-bpp-run-v5`；server 不解压 | `S:src/bundle/open.ts:291-302`；`S:AGENTS.md:17` |
| Screenshot | 可选；仅 JPEG/WebP；Run-only 必须省略 `screenshot` key，写 `null` 会被拒 | `S:src/bundle/open.ts:327-350` |

大小上限均为含等号的最大合法值：

| 对象 | 最大字节数 | 证据 |
|---|---:|---|
| Bundle | 8_388_607 | `S:src/limits.ts:2` |
| Manifest | 2_097_152 | `S:src/limits.ts:3` |
| Run segment | 2_097_151 | `S:src/limits.ts:4` |
| Screenshot segment | 1_048_576 | `S:src/limits.ts:5` |
| `projection` 经 `JSON.stringify` 后 | 524_288 | `S:src/limits.ts:6` |

### 1.2 manifest 运行时校验

- `bundle_id` 是 canonical 大写 ULID：`^[0-7][0-9A-HJKMNP-TV-Z]{25}$`（`S:src/bundle/manifest.ts:1`）。
- identifier 是 `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$`；适用于 `run_id`、`player_account_id`、`battle_id`、`combat_kind`、`result` 和双方 `account_id`（`S:src/bundle/manifest.ts:2`；`S:src/bundle/open.ts:137-181`）。
- SHA-256 是 64 位小写 hex（`S:src/bundle/open.ts:21`）。
- `created_at_ms` 是 `0..8_640_000_000_000_000` 的 safe integer；它决定 R2 object key 的 UTC 日期段（`S:src/bundle/open.ts:281,370-375`）。
- `run_format_version=5`；`projection.run` 必须是 object；`projection.battles` 最多 30 条且 `battle_id` Bundle 内唯一（`S:src/bundle/open.ts:283-325`）。
- 每条 `battle.player.account_id` 必须严格等于 `run.player_account_id`（`S:src/bundle/open.ts:214-216`）。
- combatant 9 个字段全部存在：`account_id, display_name, hero_id, hero_name, rank, rating, level, prestige, victories`。`display_name` 不可为 null、最多 256 UTF-16 code units；其余 nullable 字段必须显式写值或 `null`（`S:src/bundle/open.ts:192-208`）。
- Battle 12 个字段全部存在；`recorded_at_ms/day/hour` 必须是非负 safe integer；`encounter_id/winner_combatant_id/loser_combatant_id` 是 nullable string，不是 identifier（`S:src/bundle/open.ts:210-234`）。
- Screenshot 8 个字段全部存在；`width/height≥1`、`quality=1..100`（`S:src/bundle/open.ts:327-350`）。

### 1.3 `POST /bundles`

- 无鉴权；`run.player_account_id` 是 caller assertion（`S:docs/api-reference.md:174`）。
- 必填：
  - `Content-Type: application/x-bpp-bundle-v5`
  - `Content-Length: <positive decimal>`
  - `Content-Digest: sha-256=:<standard-base64-of-complete-bundle-sha256>:`
- header 解析和错误码由 `S:src/modules/bundle-ingest.ts:15-48` 强制。
- 首次提交：`201 {outcome:"stored"}`。
- 同 `bundle_id`、同完整 digest：`200 {outcome:"duplicate"}`。
- 同 `bundle_id`、不同字节：`409 bundle_id_conflict`。
- 同 `run_id`、不同 `bundle_id`：`409 run_already_bundled`。
- 前一次已写 R2、D1 提交失败时，同字节重试会自动恢复并返回 `201`（`S:src/modules/bundle-ingest.ts:172-230`；`S:test/modules/ingest-recovery.test.ts:40-51`）。
- 错误 envelope 为 `{error:{code,message,retryable,request_id,details?}}`；客户端不匹配 `message`（`S:docs/api-reference.md:34-48`）。

### 1.4 Ghost Battle

- `GET /ghost-battles?player_account_id=...&limit=...`；未知参数 400；`limit` 默认 200、范围 1..200（`S:src/modules/ghost-battle-discovery.ts:53-81`）。
- 限流 60 次/60 秒/IP；超限 429 + `Retry-After: 60`，限流发生在 query 解析和 D1 之前（`S:wrangler.toml:36-42`；`S:src/modules/ghost-battle-discovery.ts:33-50`）。
- 回看恰好 5 天，排序 `recorded_at_ms DESC, battle_id DESC`（`S:src/limits.ts:13`）。
- 每行自带 7 天 R2 presigned `download_url` 和 `download_expires_at_ms`；下载对象是完整 Bundle，Worker 不代理（`S:src/presigner.ts:43-84`）。
- URL 无 refresh 路由；过期后重新调用 discovery 获取新签名。

Ghost projection 还有一条产品行为必须显式接受：

- 只有 `opponent.account_id == uploader`，或 opponent 已在 `bundle_uploaders` 中，projection 才写入；uploader 只在成功 commit 的最后一条语句登记；过滤掉的历史永不回填（`S:src/modules/bundle-commit.ts:46-51,150-199`；`S:docs/api-reference.md:333`）。
- 因此首次部署需要自举：B 先成功上传一个 Bundle 注册身份，之后 A 上传与 B 的新 Battle，B 才能发现它。A 在 B 注册前已上传的 Battle 永远不会补出现。
- API 对未知账号和“当前没有合格 projection”都返回 `200 {"battles":[]}`，客户端不能伪装成可区分的状态；空态文案必须说明上述准入条件。

### 1.5 Screenshot 与 BazaarDB

- Screenshot 缺失永不使 Bundle 无效（`S:CONTEXT.md:11`）。
- 只有带 Screenshot 的 Bundle 创建 BazaarDB delivery（`S:docs/bazaardb-delivery-integration.md:11-12,397`）。
- mod 的策略是：仅当该 Run 冻结的 `BazaarDbUploadEnabled=true` 时尝试打入 Screenshot；账号 link 状态不改变 Bundle wire。此产品选择列在 §11-Q1。

---

## 2. V4 代码地图与最终处置

### 2.1 wire 层：整体替换

| V4 路径 | 当前职责 | V5 处置 |
|---|---|---|
| `M:src/BazaarPlusPlus.ModApi/ModApiRoutes.cs` | `/run-bundles`、replay-link、Screenshot 路由 | 改成 `/bundles`、`/ghost-battles`、`/health` |
| `ModApiUploadDefaults.cs` | V4 基址、20s/180s/60s | 基址改 V5；保留 20s/180s；Bundle 请求超时改 120s |
| `Clients/RunBundleClient.cs`、`Http/RunBundleMultipartContent.cs` | multipart metadata + artifact | 删除；由 `BundleUploadClient` 替代 |
| `Clients/BazaarDbSnapshotClient.cs` 及 DTO | JSON+base64 独立通道 | 删除；Screenshot 只存在于 Bundle |
| `Clients/GhostBattleClient.cs` | discovery + replay-link 两阶段 | 重写为 discovery + presigned Bundle GET |
| `Models/RunBundleUploadRequest.cs`、`RunBundleArtifactCodec.cs` | V4 wire/payload | 删除；由 V5 manifest 和 `RunPayloadV5Codec` 替代 |
| `ModApiHealthClient.cs` | 解析 `server_time_utc` | 改为 `{status,server_time_ms}`（`S:src/http/routes.ts:12-15`） |
| `BazaarDbLinkClient.cs` | 调 bazaardb.gg link redeem | 保留；不属于 V5 server |
| `ModApiJsonPost.cs` | link client 仍使用的通用 JSON POST | 保留（`M:src/BazaarPlusPlus.ModApi/Clients/BazaarDbLinkClient.cs:82,100,108`） |
| `ModApiErrorFormatter.cs` | 通用截断/HTTP 文案 | 通用方法保留；V5 envelope 解析放入新 `ModApiErrorEnvelope`，不破坏 link client |
| `MessagePackGzipCodec.cs`、`BppHttpClientFactory.cs` | 通用 codec/HTTP 工厂 | 保留 |

`BazaarPlusPlus.ModApi` 项目引用和 installer 的六程序集拓扑不变（`M:src/BazaarPlusPlus/BazaarPlusPlus.csproj:98,202,297`）。

### 2.2 Run 与 replay 事实采集：保留，去掉复制上传状态

保留：

- `RunLifecycleModule`、`RunLoggingModule`、`RunLogSessionManager`、`RunLogRecordMapper`、`QueuedRunLogStore` 的事实采集和异步写队列。
- PvP 链：`CombatReplayCaptureService → PvpBattleSnapshotCollector → ReplayPersistenceOrchestrator`。
- replay 落盘与 drain 事件；Run completion 的 2 秒 grace 仍是本地事实采集的结束机制（`M:src/BazaarPlusPlus/Game/RunLogging/RunLoggingModule.cs:586-621`）。seal 另有输入等待，不把 2 秒 completion 当成 replay 必然齐备。

删除：

- `ReplicatedRunLogStore`、`RunSyncStateStore`、`run_sync_state`。
- `BattleReplaySyncStateStore`、`ReplayPersistenceOrchestrator.MarkReplayDirty` 调用、`battles.replay_dirty/replay_last_*` 上传列。
- `RunLogSchema.UploadPayloadSchemaVersion`。
- `Game/RunLogging/Upload/` 全目录。

必要的 V5 改动不是采集语义变化：Run 激活和完成前各尝试一次把当前 profile account ID set-once 写入 `runs.player_account_id`，并在 Run 激活时冻结 `bundle_screenshot_requested`。这两个字段是 seal 输入，不是 V4 dirty 状态。

### 2.3 Screenshot：保留采集，删除独立通道

保留 `Game/Screenshots/` 中的屏幕识别、稳定性等待、RGBA 捕获、PNG 原子写、`run_screenshots` 元数据和 Screenshot 总开关。

改动：

- `EndOfRunCaptureWorkflowCore` terminal 后发布轻量 `ScreenshotCaptureTerminal`，只含 `run_id`、artifact status 和“元数据是否已持久化”。发布本身不做 I/O。
- `BundleSealCoordinator` 的订阅回调只向后台队列 `TryWrite`；SQLite 查询和 JPEG 编码不在同步 event bus 回调里执行。`InMemoryBppEventBus.Publish` 是同步派发（`M:src/BazaarPlusPlus/Core/Events/InMemoryBppEventBus.cs:37-75`）。
- BazaarDB 设置 entry 移出 `Game/Screenshots/Upload/`，改名 `BazaarDbBundleSettingsDockEntry`，保留相同配置键和强开 Screenshot 的行为；启用设置不再 arm 已删除的 Screenshot feed，因为设置只影响后续 Run 冻结的策略。
- JPEG 编码复用现有 ImageSharp 先例的长边阶梯 `[1920,1600,1280,1024,768]` 和质量阶梯 `[90,82,74,66,58,50]`（`M:src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotImagePreparer.cs:12-15`），但上限改为 1_048_576 bytes。

删除 `Game/Screenshots/Upload/` 的客户端、feed、store、cache DTO 和日志。设置 entry/label 先迁到 `Game/Screenshots/` 后再删目录，保证 UI 不消失（现注册点 `M:src/BazaarPlusPlus/BppComposition.cs:174`）。

### 2.4 通用上传 pump：保留机制，缩成单 feed

保留 `BackgroundUploadPump` 的 Unity cadence、单飞行 attempt、startup gate 和 two-point dispose（`M:src/BazaarPlusPlus/Game/Upload/BackgroundUploadPump.cs:82-104`）。

收紧接口：

- `UploadFeedKind` 只保留 `Bundle`，或在不再用于路由后直接删除；不得保留 `RunBundle/BazaarDbSnapshot` 旧值。
- 删除跨 feed `ShouldHonorArmRequest`。`UploadArmRequested` 不再带 kind；唯一 Bundle feed 收到 run exit、seal success 或手动 retry signal 即 arm。
- `UploadLogReasonCode.RunBundleNotReady` 等 V4 reason 删除/改名。
- `UploadPumpMount` 只挂一个 `BackgroundUploadPump + BundleUploadFeed`。

### 2.5 Ghost：保留本地投影和回放骨架，重写网络链

保留：

- `GhostBattleLocalProjector` 的视角反转。
- `HistoryPanelGhostBattleFilter`、HistoryPanel UI、`GhostBattlePayloadStore`、`CombatReplayRuntime.ReplayImportedBattle`。
- 既有 `OrdinalIgnoreCase` 的 win/loss 匹配已覆盖 server 小写值，不做无意义 casing 改写（`M:src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleLocalProjector.cs:91-98`；`M:src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelGhostBattleFilter.cs:51-58`）。

替换：

- discovery DTO 新增 `bundle_id/download_url/download_expires_at_ms`。
- 删除 replay-link、checkpoint 和 `sync_cursors`；V5 没有 since/cursor 参数。
- 下载完整 Bundle，交 `BundleV5Codec.Open` 验证后提取 replay。
- `result="unknown"` 在本地投影为 null/Unknown 展示态，不直接把字符串 `unknown` 当用户结果。
- V5 response 没有 hand/skill count；下载前 UI 显示未知（`null`/`—`），不能显示为 0；Bundle 下载成功后从 payload 回填。

---

## 3. 目标模块与 seam

```text
┌──────────────────────── BazaarPlusPlus.ModApi ────────────────────────┐
│ BundleV5Codec        BundleUploadClient        GhostBattleClient      │
│ pure build/open      HTTP production adapter   discovery/R2 adapter    │
└───────────────┬───────────────────────┬───────────────────────┬───────┘
                │                       │                       │
┌───────────────▼──────────┐ ┌──────────▼──────────┐ ┌──────────▼──────────┐
│ BundleSealCoordinator    │ │ BundleUploadFeed    │ │ GhostBundleConsumer │
│ readiness + budget +     │ │ outbox lifecycle + │ │ sync + refresh +    │
│ crash recovery + seal    │ │ response state     │ │ verified extraction │
└───────────────┬──────────┘ └──────────┬──────────┘ └──────────┬──────────┘
                │                       │                       │
        SQLite / payload files / immutable BundleOutbox / ghost cache
```

外部接口保持小：

```csharp
BundleBuildResult BundleV5Codec.Build(BundleBuildInput input);
OpenedBundle BundleV5Codec.Open(ReadOnlyMemory<byte> bytes);

void BundleSealCoordinator.Signal(BundleSealWake wake); // non-blocking
Task<SealPassResult> BundleSealCoordinator.ReconcileAsync(CancellationToken token);

Task<BundleUploadResponse> BundleUploadClient.UploadAsync(
    Stream sealedFile,
    long contentLength,
    string contentDigest,
    CancellationToken token);

Task<GhostSyncResult> GhostBundleConsumer.SyncAsync(string playerAccountId, CancellationToken token);
Task<GhostReplayResult> GhostBundleConsumer.EnsureReplayAsync(string battleId, CancellationToken token);
```

实现内的 clock、random、filesystem、HTTP handler 和 SQLite adapter 可替换，供测试使用；不会把 segment offset、response 分类或 replay 裁剪规则泄漏给调用方。

---

## 4. V5 本地布局与 schema

### 4.1 路径

```text
<GameRoot>/BazaarPlusPlusV5/
├── bazaarplusplus.db
├── CombatReplays/
├── GhostBattlePayloads/
├── Screenshots/
├── BundleOutbox/                  # {bundle_id}.bundle + 短生命周期 .tmp
├── CombatReplayVideos/
├── EncounterPreview/preview-plans.json
├── BazaarAgent/
├── tenwin_builds.json
└── voice-lines.json
```

`IPathProvider` 只新增一个 `DataRootDirectoryPath`，outbox 和 ghost store 在各自实现内从根派生，避免把每个子目录都扩成 interface 成员。必须同步更新生产实现和三处测试实现（`M:tests/Storage.Tests/TempDirPathProviderTests.cs:143`、`M:tests/RunLoggingSqliteStore.Tests/Program.cs:427`、`M:tests/RunLoggingSqliteRecovery.Tests/Program.cs:183`）。

以下非 `BepInExPathProvider` 引用也必须改为 V5：

- `M:src/BazaarPlusPlus/Infrastructure/BppLog.cs:129` 的隐私脱敏根；否则 V5 绝对路径会泄漏到日志。
- `M:src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentBepInExOptions.cs:12` 及 ADR-0006 的 BazaarAgent 路径。
- `TenWinBuildCatalogFactory.cs:42`、`VoiceLinesCatalogFactory.cs:42`。
- 架构/隐私/远程 catalog 测试中的对应字面量。

supporter 临时缓存也统一使用 `%TEMP%/BazaarPlusPlusV5`，避免本地路径继续携带旧数据根名称；它仍不属于运行时 Bundle 数据管线。

### 4.2 fresh schema

V5 数据库从 schema v1 开始，不执行 V4 ALTER/migration。保留事实表时删除所有 V4 sync 列/表；`runs` 新增：

`RunLogSchema.LocalDatabaseSchemaVersion=1`、`RowSchemaVersion=1`；`UploadPayloadSchemaVersion` 删除。版本 1 描述的就是这里的 fresh V5 schema，不与 V4 的 18/11 比较。

```sql
player_account_id            TEXT NULL,
bundle_screenshot_requested  INTEGER NOT NULL DEFAULT 0
```

`player_account_id` set-once：Run 激活时写入；完成前再补一次；仍为空时，sealer 只允许使用该 Run 所有非空 Battle player IDs 的唯一共识值回填。历史 Run 永不直接使用“当前登录账号”。

新增 seal job：

```sql
CREATE TABLE bundle_seal_jobs (
  run_id                    TEXT PRIMARY KEY
      REFERENCES runs(run_id) ON DELETE CASCADE,
  state                     TEXT NOT NULL DEFAULT 'waiting',
      -- waiting | sealing | terminal_failure
  player_account_id         TEXT NULL,
  screenshot_requested      INTEGER NOT NULL,
  screenshot_state          TEXT NOT NULL DEFAULT 'waiting',
      -- not_requested | waiting | available | unavailable | timed_out
  input_deadline_at_utc     TEXT NOT NULL,
  bundle_id                 TEXT NULL UNIQUE,
  created_at_ms             INTEGER NULL,
  attempts                  INTEGER NOT NULL DEFAULT 0,
  last_attempt_at_utc       TEXT NULL,
  last_error_code           TEXT NULL,
  last_error_detail         TEXT NULL
);
```

新增 outbox：

```sql
CREATE TABLE bundle_outbox (
  bundle_id                 TEXT PRIMARY KEY,
  run_id                    TEXT NOT NULL UNIQUE, -- 故意无 FK
  file_name                 TEXT NOT NULL UNIQUE,
  content_sha256_hex        TEXT NOT NULL,
  content_digest            TEXT NOT NULL,
  total_bytes               INTEGER NOT NULL,
  has_screenshot            INTEGER NOT NULL,
  sealed_at_utc             TEXT NOT NULL,
  status                    TEXT NOT NULL DEFAULT 'pending',
      -- pending | uploaded | permanent_failure
  attempts                  INTEGER NOT NULL DEFAULT 0,
  last_attempt_at_utc       TEXT NULL,
  last_error_code           TEXT NULL,
  last_error_detail         TEXT NULL,
  server_request_id         TEXT NULL,
  server_outcome            TEXT NULL,
  uploaded_at_utc           TEXT NULL
);
```

无 FK 是有意设计：HistoryPanel 会直接删除 `runs`（`M:src/BazaarPlusPlus/Game/HistoryPanel/Storage/HistoryPanelRepository.cs:701-722`），已 seal Bundle 的上传/审计不能阻断用户删除。

Ghost 行新增：

```sql
bundle_id                       TEXT NULL,
download_url                    TEXT NULL,
download_url_expires_at_ms      INTEGER NULL,
ghost_replay_state              TEXT NULL,
    -- remote_available | local_ready | unavailable_payload | expired
ghost_replay_unavailable_reason TEXT NULL
```

删除 `sync_cursors`、`run_sync_state`、`bazaardb_snapshot_uploads`；删除 Battle 上所有 replay upload dirty/retry 列。`source` 只使用合法大写 `LOCAL/GHOST`。

---

## 5. Bundle codec 与 Run payload

### 5.1 `BundleV5Codec`

建议布局：

```text
src/BazaarPlusPlus.ModApi/Bundle/
├── BundleLimitsV5.cs
├── UlidV5.cs
├── BundleManifestV5.cs
├── BundleManifestV5Writer.cs
├── BundleV5Codec.cs
└── RunPayloadV5Codec.cs
```

`Build` 内部完成：

1. 校验 ULID/identifier/nullability/range/uniqueness/player identity。
2. 计算 Run/Screenshot segment SHA-256。
3. 用固定键序写紧凑 UTF-8 manifest，无 BOM。
4. 计算 offset、长度和 16-byte prefix。
5. 计算完整 Bundle SHA-256，产出 lowercase hex 和 `Content-Digest` header。
6. 对 manifest/projection/segment/Bundle 全部上限做本地拒绝。

固定键序与 server fixture 一致：

- root：`bundle_id,bundle_version,created_at_ms,run[,screenshot]`
- run：`run_id,player_account_id,run_format_version,projection,payload`
- projection：`run,battles`
- battle：contract 的 12 字段顺序
- combatant：contract 的 9 字段顺序
- segment descriptor：`offset,length,sha256,content_type[,width,height,quality,captured_at_ms]`

字符串写出遵循 `JSON.stringify` 等价语义；整数只写十进制；Run-only 完全省略 `screenshot`。server 不要求键序，但该确定性规则用于黄金向量和可复现诊断。

V5 mod 固定写 `projection.run={}`；server 只要求它是 object。Run 的完整事实只进入压缩 payload，避免在 manifest 维护第二份易漂移的摘要。

`Open` 验证 prefix、fatal UTF-8、manifest 字段、布局、整包边界、每段 digest，且设置读取上限；Ghost 下载不能在验证前解压。损坏样本 reason 至少区分 `invalid_prefix`、`manifest_not_json`、`segment_digest_mismatch`、`segment_out_of_bounds`。

### 5.2 Run payload

server 不读取 payload，因此 mod 定义一个公开 MessagePack DTO 图：

```csharp
public sealed class RunPayloadV5
{
    public int PayloadFormatVersion { get; set; } = 5;
    public string RunId { get; set; }
    public string PlayerAccountId { get; set; }
    public RunFactsV5 Run { get; set; }
    public List<RunEventV5> Events { get; set; }
    public List<RunBattleV5> Battles { get; set; }
    public List<string> ReplayableBattleIds { get; set; }
    public PayloadDegradationV5 Degradation { get; set; }
}

public sealed class RunBattleV5
{
    public string BattleId { get; set; }
    public BattleFactsV5 Facts { get; set; }
    public BattleParticipantsV5 Participants { get; set; }
    public BattleCardSnapshotsV5? Snapshots { get; set; }
    public BattleReplayV5? Replay { get; set; }
}
```

编码为 MessagePack（沿用现有 resolver）再 gzip `CompressionLevel.Optimal`。整个 DTO 图保持 `public`，符合 Unity/Mono runtime 约束。

字段来源固定如下，实施时不再另造一套游戏探测：

| payload 部分 | 字段/来源 |
|---|---|
| `RunFactsV5` | `runs` 的 hero、game_mode、seed、started/ended time、status、day/hour、victories/losses、起止 rank/rating、rating delta、max_health/prestige/level/income/gold、build_channel，以及当前 mod version |
| `RunEventV5` | `run_events` 的 `seq/ts_utc/kind/payload_json`，按 seq 升序；保留原结构化 JSON，不重新解释每个 event kind |
| `BattleFactsV5` | `battles` 的 recorded time、day/hour、encounter/combat/result、winner/loser、final 标记 |
| `BattleParticipantsV5` | 双方 account/name/hero/rank/rating/level/prestige/victories；income/gold/hand/skill count 可作为 payload-only 扩展，不进入 manifest combatant |
| `BattleCardSnapshotsV5` | `battle_snapshots` 四组 card set，沿用现有公开 snapshot DTO 语义 |
| `BattleReplayV5` | `CombatReplayPayloadStore` 的 Spawn/Combat/Despawn 三组原生 MessagePack bytes |

Run payload 是 mod、Ghost 客户端、analyzer 和 BazaarDB 之间的下游契约，不是 Worker 校验契约。P1 同时落一份 `docs/contracts/run-payload-v5.md` 和一个 payload fixture；DTO 字段或 codec 变更必须同步更新文档、fixture 和 downstream gate。

identity/version 双重校验：Ghost 消费时要求 payload `RunId/PlayerAccountId` 与 manifest 相同；不一致则拒绝并把 ghost 行标记为 `unavailable_payload`。

### 5.3 projection 归一化

Battle 只有同时满足以下条件才进入 projection 候选：

- `source='LOCAL'`，有完整 snapshots 和 Spawn/Combat/Despawn replay。
- `battle_id/player_account_id/opponent_account_id/combat_kind` 是合法 identifier。
- player ID 严格等于冻结的 Run player ID。
- `recorded_at_ms/day/hour` 可解析且非负；无效 Battle 只进 payload facts，不伪造 0。
- opponent account ID 缺失或任一必填 identifier 无效时，只进 payload，不进 projection。

字段映射：

- `display_name`: null → `""`；超过 256 UTF-16 code units 时安全截断，不拆 surrogate pair。
- `hero_id`: 当前没有稳定 ID 来源，写 `null`；`hero_name/rank` 使用本地值，超过 256 时写 `null`。
- nullable 数字 `rating/level/prestige/victories`: 负值 → `null`。
- `result`: null/空 → `"unknown"`；已知 `win/loss` 原样小写。
- `encounter_id/winner_combatant_id/loser_combatant_id`: null 原样；超过 256 时写 `null`。
- `is_final_battle`: 以完成 Run 的最后一场 LOCAL Battle 为 true；若该 Battle 未进入最终 projection，则 projection 中没有伪造的 final Battle。

候选按 `recorded_at_ms ASC,battle_id ASC` 稳定排序；超过 30 时保留最新 30 条，manifest 中仍按时间升序写出。

### 5.4 payload 大小预算与 projection 联动

`RunPayloadComposer` 一次返回 gzip bytes 和最终 `ReplayableBattleIds`。确定性降级顺序：

1. 先尝试全部 facts/events/snapshots/replay。
2. 超限时，从不在最新 30 个 projection 候选中的最早 Battle 开始，成对移除 `Snapshots + Replay`，保留 facts/participants。
3. 仍超限时，从 projection 候选中最早 Battle 开始成对移除 `Snapshots + Replay`，并同步把该 ID 移出 manifest projection。
4. 仍超限时移除剩余非 replayable snapshots。
5. 极端情况下按最老优先截断非 terminal Run events；Run identity、最终 Run facts和 Battle facts不删。
6. 若最小 payload 仍超过 2_097_151，seal job 进入一次性 `terminal_failure/payload_too_large`，不在每次启动重复刷错。

不允许只删 Replay、保留 projection，或 projection 指向 payload 中不可回放的 Battle。下载端即使遇到第三方/旧 bug 产物缺 replay，也会持久标记 `unavailable_payload`，不反复下载。

---

## 6. Seal 状态机与崩溃恢复

### 6.1 触发

`RunLoggingModule` 当前没有完成事件，完成点只调用 `sessionManager.CompleteRun`（`M:src/BazaarPlusPlus/Game/RunLogging/RunLoggingModule.cs:608-621`）。V5 不新增一个承载重 I/O 的同步完成 handler。

`BundleSealCoordinator` 作为独立 `IBppFeature`：

- Start 时启动单后台 worker 并立即 reconciliation。
- 订阅 `RunInitializedObserved`、`RunLifecycleChanged`、`CombatReplayPersistenceDrained`、`ScreenshotCaptureTerminal`；handler 只记录 run ID/向 channel 写 wake。
- worker 每 5 秒兜底扫描一次，创建 `completed=1 AND status='completed'`、Ranked、非 PTR、无 outbox/job 的 seal job。
- coordinator 注册在 `RunLoggingModule` 之前，使反向 `Stop()` 时 Run logging 先完成强制落盘，coordinator 再做一次只持久化状态的最终 reconcile；teardown 不等待 Screenshot 编码或网络上传（feature registry 反向停止见 `M:src/BazaarPlusPlus/Core/Runtime/BppFeatureRegistry.cs:37-47`）。
- 恢复与 seal 不依赖 upload pump，因此 PTR 门禁或无网络不会阻止本地状态收敛。

### 6.2 输入收敛

seal job 的 `input_deadline_at_utc = runs.ended_at_utc + 2 minutes`。每次 reconcile：

1. **identity**：优先 `runs.player_account_id`；否则使用所有非空 LOCAL Battle player IDs 的唯一共识值并回填；冲突或截止后仍缺失 → `terminal_failure/identity_unavailable`。
2. **replay**：截止前有任何 payload store 状态为 Missing/Pending，则继续等待；收到 drain signal 立即重试。截止后 missing/invalid/unreadable Battle 退化为 facts-only，且不进 projection。
3. **Screenshot**：
   - `screenshot_requested=0` → `not_requested`，不等待。
   - primary `run_screenshots` 行和 PNG 均存在 → `available`。
   - terminal 明确失败/无可用 metadata → `unavailable`。
   - 截止仍无结果 → `timed_out`。
4. identity 确定且 replay/Screenshot 已 terminal 后才进入 `sealing`。

Screenshot encoder 失败、超时 30 秒、文件不存在或所有质量阶梯仍超限，都只把该 job 改为 Run-only；不得把 job 标记为整体失败。

### 6.3 seal 发布协议

```text
DB: waiting
  1. transaction: allocate and persist bundle_id + created_at_ms, state=sealing
  2. read immutable Run facts / settled replay / Screenshot decision
  3. compose payload + final projection
  4. BundleV5Codec.Build + self-open verification
  5. AtomicFileWriter → BundleOutbox/{bundle_id}.bundle
  6. transaction: INSERT bundle_outbox(pending), DELETE bundle_seal_jobs
  7. publish arm signal
DB: pending outbox
```

bundle ID/time 在文件写前持久化，崩溃恢复使用同一个 ID：

- 分配后、写文件前崩溃：重新 build。
- rename 后、outbox INSERT 前崩溃：启动时发现 job 的同名 `.bundle`，用 `Open` 和 digest 校验后直接 adopt；无须换 ID。
- outbox INSERT 后、job DELETE 前崩溃：以 outbox 为真，删除重复 job。
- `{bundle_id}.bundle.<guid>.tmp` 超过 24 小时删除；未知且无 job/outbox 的 `.bundle` 不上传，保留 24 小时后清理。

一旦进入 outbox，文件字节不可修改。唯一例外是磁盘文件本身丢失/损坏：若源 Run 仍在，同一事务删除 pending outbox 行并重新建立 seal job，随后生成新 ULID 和新文件；若旧文件其实已被 server 接收，重传新 Bundle 会以 `run_already_bundled` 成功等价收敛。若用户已删除源 Run，则不能重造，outbox 直接进入 `permanent_failure/source_run_deleted`。

---

## 7. 上传状态机与文件生命周期

### 7.1 上传 attempt

`BundleUploadFeed` 每次取最多 3 条：

```sql
SELECT * FROM bundle_outbox
WHERE status='pending'
ORDER BY attempts ASC, sealed_at_utc ASC
LIMIT 3;
```

每条顺序处理：

1. 打开文件；检查文件长度；流式重算完整 SHA-256 与记账值相同。
2. rewind/reopen 后用 `StreamContent` 上传，不用 `ReadAllBytes`；显式设置三个必填 header。
3. 120 秒请求超时。
4. 解析成功 body 或 V5 error envelope，按下表原子写回。

泵 cadence 保持 startup 20 秒、固定 180 秒；run exit、seal success 和 transient retry arm 可提前唤醒。固定间隔足以，因为 server 幂等且 R2 orphan 恢复要求同字节重试。

### 7.2 响应分类

| 条件 | 本地结果 | 文件 |
|---|---|---|
| `201` + `outcome=stored` | `uploaded/stored` | 立即删 |
| `200` + `outcome=duplicate` | `uploaded/duplicate` | 立即删 |
| `409 run_already_bundled` | `uploaded/run_already_bundled` | 立即删 |
| `409 bundle_id_conflict` | `permanent_failure` | 保留 7 天诊断 |
| `400/411/413/415/422` + 已知 envelope | `permanent_failure`（本地构造/请求 bug） | 保留 7 天 |
| envelope `retryable=true`，或 408/429/5xx | transient | 保留并重试；尊重 `Retry-After` |
| 网络、超时、无法解析响应、格式错误的 2xx | transient | 保留并重试，避免误删 |
| 其余可解析 envelope | 按 `retryable`；false → permanent，true → transient | 对应处理 |

分类只匹配 `(status,code,retryable)`，不匹配 `message`。`404 not_found/405 method_not_allowed` 会按 `retryable=false` 进入 permanent，并保留诊断文件；它们代表客户端基址/route 发布错误，不应无限打流量。

### 7.3 生命周期

- `pending` transient 文件保留最多 14 天；这是 mod 的离线磁盘上界/数据新鲜度策略，不是 analyzer 契约。过期后标 `permanent_failure/local_retention_expired` 并删除文件。
- outbox 总体再设 512 MiB 软上限；超过时只从最老、已超过 14 天的 pending 或 permanent 文件清理，不提前丢尚在保留期内的数据。
- `permanent_failure` 文件保留 7 天，之后删文件；行长期保留。
- 成功响应先事务写 `uploaded`，事务提交后再删文件；崩溃在两步之间只会留下可安全清理的多余文件，不会丢失 pending bytes。`uploaded` 行长期保留。
- 删除本地 Run 不删除 outbox 行；删除尚未 seal 的 Run 会经 `ON DELETE CASCADE` 删除 seal job。

### 7.4 日志治理

新增 closed scope `BundlePipeline`，在 `BppLogSchema` 注册 prefix `bundle_pipeline`；事件定义在 `[BppLogEventSource] BundlePipelineLogEvents` 的 `static readonly` 字段中：

- `bundle_pipeline.seal.started/succeeded/degraded/failed`
- `bundle_pipeline.upload.started/stored/duplicate/transient/permanent`
- `bundle_pipeline.ghost.sync_succeeded/download_failed/...`

每个 event ID 至少三段、dotted snake，满足 `M:src/BazaarPlusPlus/Infrastructure/Logging/Core/BppLogEventCatalog.cs:186-198`。degradation 的 `category` 必须进入 `BppLogStormPolicy` key。同步删除 `PluginHandlerId.RunBundleUploadFeed` 和 `PluginLogIdentity` 的旧类型映射（`M:src/BazaarPlusPlus/PluginLogEvents.cs:62`；`M:src/BazaarPlusPlus/PluginLogIdentity.cs:51-52`）。

---

## 8. Ghost 消费状态机

### 8.1 discovery

触发保持“进入 HistoryPanel Ghost section 时一次 + 防重入”，无后台 timer（现入口 `M:src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs:92-97,925-933`）。

```text
SyncAsync
  1. 解析当前 player_account_id；无账号 → deferred
  2. 若本地 rate-limit cooldown 未过 → 不发请求
  3. GET /ghost-battles?...&limit=200
  4. 429 → 解析 Retry-After（delta-seconds 或 HTTP-date），持久到内存 cooldown
  5. upsert GHOST rows，最新 URL/expiry 总是覆盖
  6. 未下载且 recorded_at < now-5d → expired/soft delete
```

空态文案：`暂无可发现的 Ghost Battle。双方都至少成功上传过一个 Bundle 后，后续对局才会出现；此前被过滤的历史不会回填。`

### 8.2 下载与验证

```text
EnsureReplayAsync(battleId)
  1. local_ready → 直接返回缓存
  2. URL 将过期/已过期 → discovery 刷新一次
  3. GET presigned URL（不带 mod server auth/header）
  4. Content-Length > 8_388_607 或流读取越界 → 拒绝
  5. BundleV5Codec.Open
  6. manifest.bundle_id == row.bundle_id
  7. gunzip + MessagePack decode RunPayloadV5
  8. payload RunId/PlayerAccountId == manifest
  9. 找到 battleId，且 snapshots + Spawn/Combat/Despawn 均完整
 10. 重建 GhostBattlePayload，原子保存并回填 hand/skill counts
```

失败终态：

- 403/404：刷新 discovery 后只重试一次；仍失败 → `expired`。
- Bundle/payload 损坏、identity 不同、目标 Battle 不存在或 replay 不完整 → `unavailable_payload`，持久记录 reason，不再反复下载。
- 网络/5xx → transient，不改成永久不可用。
- URL 过期但已在本地缓存 → `local_ready` 不受影响。

`ReplayAvailable` 不再硬编码 true（V4 当前点 `M:src/BazaarPlusPlus.ModApi/Clients/GhostBattleClient.cs:230`）；UI 从 `ghost_replay_state` 派生按钮状态。

---

## 9. 删除与改名清单

### 9.1 删除

- `M:src/BazaarPlusPlus.ModApi/Clients/RunBundleClient.cs`
- `M:src/BazaarPlusPlus.ModApi/Clients/BazaarDbSnapshotClient.cs`
- `M:src/BazaarPlusPlus.ModApi/Http/RunBundleMultipartContent.cs`
- `M:src/BazaarPlusPlus.ModApi/Models/RunBundleUploadRequest.cs`
- `M:src/BazaarPlusPlus.ModApi/Models/BazaarDbSnapshotUploadRequest.cs`
- `M:src/BazaarPlusPlus.ModApi/RunBundleArtifactCodec.cs`
- `M:src/BazaarPlusPlus/Game/RunLogging/Upload/` 全目录
- `M:src/BazaarPlusPlus/Game/Screenshots/Upload/` 在 settings entry/label 迁出后的其余全目录
- `M:src/BazaarPlusPlus.Storage/Upload/RunSyncStateStore.cs`
- `M:src/BazaarPlusPlus.Storage/Upload/BattleReplaySyncStateStore.cs`
- `M:src/BazaarPlusPlus.Storage/RunLog/Replication/ReplicatedRunLogStore.cs`
- ghost checkpoint repository 方法和 `sync_cursors`
- `tests/BazaarDbScreenshotUploadService.Tests/`
- `tests/BazaarDbScreenshotUploadStore.Tests/`
- 仅供上述测试使用的 `tests/Shared/ScreenshotUploadTestHelpers.cs`
- 无 csproj 的 `tests/RunLoggingSqliteSchema.Tests/bin,obj` 残留

### 9.2 改名/迁移

- `BazaarDbSnapshotUploadSettingsDockEntry` → `BazaarDbBundleSettingsDockEntry`，移到 `Game/Screenshots/`。
- `UploadFeedKind.RunBundle` → `Bundle`（若 kind 仍保留用于日志）；`BazaarDbSnapshot` 删除。
- V5 代码和日志不使用 server `CONTEXT.md` 禁用旧名词。架构测试只扫描 V5 Bundle/Upload scope，不能误伤合法游戏领域的 `snapshot`（如 card snapshot）。

### 9.3 最终字符串门禁

- `src/BazaarPlusPlus.ModApi` 不得出现 `mod-api-v4`、V4 route 或 multipart 类型。
- Game upload/bundling 目录不得引用 V4 feed/store/type。
- GameRoot 数据路径都指向 `BazaarPlusPlusV5`，但明确排除 `BPPSupporterCatalog` 的 temp cache。
- `BppComposition` 只注册一个 Bundle pump；不注册独立 Screenshot feed。

---

## 10. 验证计划

### 10.1 黄金向量

新增 `tests/BundleV5Codec.Tests`，沿仓库约定使用 `net10.0` exe-runner：

- vendor `S:contracts/v5/fixtures/` 的 5 个文件，并记录 server commit SHA。
- csproj 使用 `Content Include="fixtures/**/*" CopyToOutputDirectory="PreserveNewest"`；运行时从 `AppContext.BaseDirectory/fixtures` 读取，不能依赖工作目录。
- `BazaarPlusPlus.ModApi` 对 MessagePack 是 compile-only；测试项目像现有 ModApi 测试一样显式引用 `ManagedPath/MessagePack.dll`，否则 `RunPayloadV5Codec` runtime round-trip 会失败（`M:src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj:15-17`）。

断言：

1. 从 `run-only.manifest.json` **读取字段值后重新紧凑序列化**，不能直接使用 pretty JSON 文件字节。产物必须逐字节等于 `run-only.bundle.b64` 解码的 399 bytes。
2. manifest 374 bytes；payload `1f8b08000504030201`；完整 SHA-256 `6959c53bf3b3b8f38e7aefb2edf13d3ff4ddb0414e95cf4c0460466a7000b36d`；Content-Digest `sha-256=:aVnFO/OzuPOOeu+y7fE9P/TdsEFOlc9MBGBGanAAs20=:`。
3. corrupt magic 与 segment digest mismatch 均被 `Open` 拒绝，reason 对齐 fixture。
4. 含 Screenshot、30 battles、非 ASCII display name 的 Build→Open round-trip。
5. 31 battles、重复 ID、player/uploader 不同、非法 identifier、null display name、负 day/hour、超限 segment/projection/Bundle 均在本地被拒绝。
6. 固定 clock+RNG 的 ULID 确定性、canonical 大写和单调性。

### 10.2 模块接口测试

`BundleSealCoordinator`：

- completed 后建 job；abandoned/PTR/non-Ranked 不建。
- account 在激活写入、完成补写、Battle 共识回填、冲突 terminal。
- Screenshot disabled 立即 ready；enabled 等待；success 带图；failed/deadline Run-only。
- replay pending 等 drain/deadline；deadline 后 facts-only；最终 projection 永远是 `ReplayableBattleIds` 子集。
- 大小预算每一级降级与极端 terminal。
- 崩溃点：ID 分配前后、rename 前后、outbox INSERT 前后；恢复不上传未知文件。
- 文件丢失/损坏原子 requeue，不留下阻止 re-seal 的 permanent outbox 行。

`BundleUploadFeed/Client`：

- 文件流、Content-Length、Content-Type、Content-Digest 精确。
- 201/200/409×2/422/429/503/未知 envelope/坏 2xx/网络/超时全矩阵。
- 同文件重复 attempt 请求体逐字节相同。
- uploaded 删除文件但保留行；用户删除 Run 不受 outbox 影响；7/14 天清理与 `.tmp` 清理。

`GhostBundleConsumer`：

- discovery DTO、GHOST 大写、5 天窗口、429 cooldown、URL refresh once。
- 预下载 hand/skill count 为 unknown；下载后回填。
- Bundle ID、payload identity、target battle、snapshots/replay 完整性校验。
- 不完整 payload 进入 `unavailable_payload` 且第二次点击不再发 GET。
- `unknown` result 的本地展示/过滤；既有大小写 win/loss 回归。

`ScreenshotSegmentEncoder`：

- 阶梯顺序确定；输出 ≤1 MiB；manifest 记录实际 width/height/quality。
- ImageSharp 仅留在主插件 Game 层，不引入 `BazaarPlusPlus.ModApi`，避免改变发布依赖拓扑。

### 10.3 存量测试处置

| 项目 | 处置 |
|---|---|
| `ModApi.Tests` | 重写 V5 routes/client/error envelope；保留 BazaarDB link 通用 helper 测试 |
| `CombatReplayRecording.Tests` | 删除 `run_sync_state` 插入和 V4 permanent upload 断言；保留 replay persistence，并新增 seal readiness 断言 |
| `StartupUploadRunner.Tests` | 删除双 feed/cross-kind 测试；保留 cadence、单飞行、shutdown drain；改成单 Bundle feed |
| `SettingsDockRegistry.Tests` | 改为新 settings 类型/命名空间；断言 toggle 强开 Screenshot、不会 arm 旧 feed |
| `GhostBattleSync.Tests` | 全面重写 V5 discovery/Bundle 下载/terminal state |
| `HistoryPanelRepository.Tests` | 删除 checkpoint；14→5 天；新列/upsert；DeleteRun 与无 FK outbox 回归 |
| `HistoryPanelFiltering.Tests` | 增加 `unknown` 与小写 win/loss 用例 |
| `HistoryPanelServerHealth.Tests` | `server_time_ms` |
| `Storage.Tests`、`RunLoggingSqliteStore.Tests`、`RunLoggingSqliteRecovery.Tests` | 更新 `IPathProvider.DataRootDirectoryPath` 和 fresh schema |
| `RunLoggingCapture/Module/Session.Tests` | account/Screenshot policy 冻结和新 store 组合 |
| `RunScreenshotSqliteStore.Tests`、`EndOfRunScreenshotGate.Tests` | terminal wake、Screenshot 缺失 fail-open |
| `BppLog.Tests` | V5 隐私根、新 closed scope/event governance |
| `LiveBuildRecommendations.Tests`、`VoiceSubtitles.Tests`、`RemoteEmbeddedCatalog.Tests` | V5 根路径 |
| `Architecture.Tests` | 新依赖/禁词/单 feed/公开 DTO 图门禁；保留 supporter temp 例外 |
| `BazaarDbScreenshotUpload*` | 删除项目和 shared helper |

`./run.sh test` 会扫描所有 `tests/*/*.csproj`（`M:run.sh:267-283`），上述处置必须在对应生产删除的同一阶段完成，不能把红测试留到最后。

### 10.4 端到端

本地 server：

1. server 执行 `npm install`，再执行 `npx wrangler d1 migrations apply bazaarplusplus-mod-api-v5-db --local`，然后 `npm run dev`。
2. 创建 gitignored `.dev.vars`，提供 `R2_PRESIGN_SECRET_ACCESS_KEY`、`BUNDLE_SYNC_TOKEN`、`BAZAARDB_DELIVERY_TOKEN`；README 明确缺失时 fail closed（`S:README.md:30-33`）。本地 token 可用测试随机值，不能提交。
3. 新增 `tools/BundleV5E2E` 手动 runner，不放在 `tests/` 默认扫描中；参数 `--base-url`，复用生产 codec/client。
4. 对本地 Worker：黄金 Bundle 201 → 同字节 200 duplicate → 同 bundle ID 篡改 409；真实 sealer 产物 201。local R2 不验证生产 presigned 域直下。

线上/游戏人工 smoke：

1. Ranked Run 完成，日志出现 `bundle_pipeline.seal.succeeded` 与 `bundle_pipeline.upload.stored`；重启后 outbox 无重复文件。
2. BazaarDB 开关关闭：Run-only；开启后的下一 Run：等待 Screenshot terminal，`has_screenshot=true`，响应 delivery 为 created。
3. Ghost 自举严格按顺序：账号 B 先上传任意 Bundle完成注册 → A 与 B 产生一场新 Battle → A 上传 → B discovery 看到 projection → presigned URL 下载的 bytes 与 A 原 sealed 文件一致 → 游戏内回放成功。
4. 验证 B 注册前的 A 历史 Battle 不回填，UI 空态文案正确。
5. analyzer 与 BazaarDB direct-R2 smoke 完成前不发布 V5 mod（`S:docs/deployment-runbook.md:129-130`）。

---

## 11. 分阶段实施

每阶段结束都要求 `./run.sh test` 与构建通过。V4 删除在 P2 原子完成，不保留“新 schema + 旧消费者”的不可运行中间态。

| 阶段 | 工作 | 退出标准 |
|---|---|---|
| P1 wire codec | `BundleV5Codec`、manifest writer、ULID、Reader、RunPayload codec、payload contract、fixtures | 黄金向量逐字节；损坏向量；round-trip 全绿；downstream payload fixture 固定 |
| P2 本地 cutover | V5 data root/fresh schema；冻结 identity/policy；settings 迁出；删除 V4 wire/feed/store/dirty；单 feed pump 接口；同步重写受影响测试；暂时取消 `UploadPumpMount` 注册 | 全仓构建/测试绿；游戏可记录 Run；无上传 stub、无任何 V4 网络上传装配 |
| P3 seal + upload | seal jobs/coordinator、payload budget/projection、Screenshot encoder、outbox、Bundle client/feed、重新注册单一 `UploadPumpMount`、日志 scope、crash recovery | fake HTTP 矩阵；本地 wrangler e2e；游戏 Run-only 和 Screenshot-bearing smoke |
| P4 Ghost | V5 discovery、DB 状态、Bundle download/verify/extract、UI unknown/eligibility 文案、health | 双账号自举 smoke + 回放成功；过期/损坏终态测试 |
| P5 发布收口 | scoped 架构门禁、路径/文档引用、format/lock/publish gates | `format-check`、`restore-locked`、全测试、在线 smoke、下游 direct-R2 gate |

P1 独立；P2 依赖 P1 的 DTO/schema 名称；P3 依赖 P1+P2；P4 依赖 P1 Reader 和 P2 ghost schema；P5 最后。

---

## 12. 需要与 server/产品侧讨论的问题

这些问题不授权 mod 变通；实现始终按当前 server 代码。

1. **产品：Screenshot 条件**。是否还要求 BazaarDB account 已 link？本文只按 Run 激活时的 `BazaarDbUploadEnabled`；未 link 也会创建 delivery。
2. **规范缺口**。`battle.player.account_id == run.player_account_id` 仅在 `open.ts:214-216`，应补入 `bundle-v5.md/schema`。
3. **规范缺口**。Run-only 必须省略 `screenshot`，schema 无法表达“不能为 null”的运行时差异，应明示。
4. **schema 弱于实现**。Screenshot offset、`created_at_ms` 上界、UTF-16 `maxLength` 语义应与 `open.ts` 对齐。
5. **文档缺口**。Ghost 60/60s 数值只在 `wrangler.toml`，应进入 API reference。
6. **D1 断言**。`CASE json_extract(...is_final_battle) WHEN 1` 依赖 D1 JSON boolean→1，应有端到端测试（`S:src/modules/bundle-commit.ts:161`）。
7. **fixture 缺口**。`corrupt-magic` 无 sha256/length，且现样本首字节导致 UTF-8 分支，未覆盖“合法 UTF-8 但 magic 不同”。
8. **契约漂移风险**。`contracts/v5/versions.yaml` 当前无代码/测试引用，版本接受逻辑硬编码在 prefix/open。

---

## 13. 主要风险与验收口径

| 风险 | 控制 |
|---|---|
| 账号在 Run 激活时不可用 | 完成前再补 + Battle 唯一共识；不猜历史账号；一次 terminal failure |
| replay 2 秒后仍未落盘 | sealer 独立等 2 分钟 + drain wake；超时只降级对应 Battle，projection 联动删除 |
| Screenshot 比 Run completion 晚 | seal job 等 terminal/DB row/deadline；失败 Run-only |
| 长 Run gzip 超 2 MiB | 确定性预算；projection 与 replayability 同步；最小 payload 超限才 terminal |
| crash 留半成品 | ID/time 先持久化、atomic rename、adopt 协议、`.tmp` 清理 |
| response 丢失后重复提交 | 永远重传同文件；server duplicate/orphan recovery；`run_already_bundled` 收敛 |
| 用户删除 Run 阻断 outbox | outbox 故意无 FK；专门 DeleteRun 回归测试 |
| V5 上线初期 Ghost 为空 | 明示 `bundle_uploaders` 自举和无回填；双账号顺序 smoke |
| 下载到不可回放 Bundle | 上传端 projection invariant + 下载端 terminal `unavailable_payload` |
| V5 路径未被日志脱敏 | 同步修改 `BppLog` privacy root 并跑隐私测试 |

最终验收只有四条：黄金 Bundle 逐字节一致；每个 sealed file 在重试中字节不变；Screenshot 缺失仍能成功上传；每个由本 mod 产生的 Ghost projection 都能从下载 Bundle 中重建可播放 replay。
