---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# End-of-Run Screenshots & BazaarDB Upload

一条 `end_of_run_auto` 截图管线：终局自动抓图（采集） → 可选地上传到 `bazaarplusplus-server` 供 BazaarDB 拉取（消费）。采集始终发生；上传是默认关闭的附加 feature（`BazaarDB / UploadScreenshots`），两者通过 `run_screenshots` 表解耦。

SQLite 列定义（`run_screenshots`、`bazaardb_snapshot_uploads`）统一见 [sqlite-schema-reference.md](../reference/sqlite-schema-reference.md)，本文不重复。

## 采集（Capture）

当前只保留一种来源 `end_of_run_auto`——只在终局页面自动保存一张主图。旧的 `F9` 手动截图、设置面板 `CAM` 按钮、PVP 战斗截图均已移除。

### Trigger

终局界面出现后，`EndOfRunScreenController` 上挂一个全屏透明鼠标 blocker。满足三条件才触发截图：(1) 终局界面已出现满 8 秒；(2) 本局还没成功过一次终局自动截图；(3) 用户触发了一次合法 `Continue`。8 秒窗口内 blocker 吞掉鼠标点击；窗口过后第一次合法点击会先抓图再放行原始 `Continue`，抓图失败则本局仍可再次尝试。

### Storage

- 文件：`<GameRoot>/BazaarPlusPlusV4/Screenshots/YYYY-MM-DD/<...>_final_run-<runId>.png`
- 元数据：`bazaarplusplus.db` 的 `run_screenshots` 表；运行时只写 `capture_source = end_of_run_auto`、`is_primary = 1`（`battle_id` 等通用列保留但终局截图不写 battle 维度）。

### Runtime Data Sources

抓图排队当下读取：`run_id`（controller 缓存 → `RunContext.CurrentServerRunId`）、`hero_name`、`player_rank`/`player_rating`（`RunLoggingGameDataReader.TryGetPlayerRankSnapshot`）、`player_position`（`BppClientCacheBridge.TryGetPlayerLeaderboardPosition`）、`day`（`Data.Run.Day`）、`victories_at_capture`（`Data.Run.Victories`）。

### UI Suppression

抓图时临时隐藏 Bazaar++ 自己的浮层（设置坞、combat status bar、CollectionPanel 坞按钮），避免拍进终局图。

### External Reader Contract

外部读取方拿本局主图：查询 `run_id = ?` 且 `is_primary = 1`，图片绝对路径 = `<GameRoot>/BazaarPlusPlusV4/Screenshots/` 拼接行内 `image_relative_path`。

## BazaarDB 上传（可选，默认关闭）

开启 `BazaarDB / UploadScreenshots` 后，`BazaarDbSnapshotUploadController` 在**非 live run** 时按 20s 启动延迟 + 180s 间隔扫描待上传截图，把同一批 `end_of_run_auto` 截图组装成 Snapshot DTO 推到 `bazaarplusplus-server`，BazaarDB 再通过 peek/confirm 队列从服务端拉取（模组与 BazaarDB 之间无直接连接）。翻开开关会回填历史截图——sidecar 表里缺失的 `end_of_run_auto` 行统统入队，无时间戳 cutoff。

### Client Flow

1. 每个 tick 先 `EnsureBackfilled`（`INSERT OR IGNORE` 补齐 sidecar 行），开关关闭时 tick 顶部短路、零 DB / 零网络。
2. 取最多 3 条 `status='pending'`（按 `captured_at_utc ASC`）。
3. 取 `player_account_id`（`BppClientCacheBridge.TryGetProfileAccountId`），取不到则**整轮短路跳过**，不发占位符。
4. 向 `bazaarplusplus-server` 发一次健康探测（每批次懒初始化，仅探测一次）；探测失败则**整批短路**、留 `pending` 下轮再试。
5. 每条：保留本地原始 PNG 不动，准备上传用图片 bytes（原图 `<= 2 MiB` 则直接使用；否则在 `Screenshots/UploadCache/` 生成 PNG derivative，必要时降级 JPEG derivative，最终 bytes 必须 `<= 2 MiB`）→ 组装 Snapshot DTO → `POST /bazaardb/snapshots/<snapshot_id>` → 按结果落库。
6. 结果分类：`Success`→`uploaded`；4xx（除 408/429）→`permanent_failure` 不再重试；5xx / 网络 / 408 / 429 → 留 `pending`、`attempts++` 下轮再试；文件已删 / 行不可读 / 上传 derivative 仍超过限制 → `permanent_failure`。
7. run 退出或开关 false→true 时 `ArmImmediateAttempt`，下一帧立即上传，无需等 180s。

### Server Flow

| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | `/bazaardb/snapshots/<snapshot_id>` | open | 模组上传完整 Snapshot DTO |
| POST | `/bazaardb/peek` | `Bearer <BAZAARDB_PULL_TOKEN>` | BazaarDB 领取一批待投递 DTO，响应短期预签下载 URL |
| POST | `/bazaardb/confirm` | `Bearer <BAZAARDB_PULL_TOKEN>` | BazaarDB 确认已落地的 DTO，服务端标记 done 并删除对应 R2 对象 |

Ingest 把请求体原样写入私有 R2（`bazaardb/snapshots/<snapshot_id>/<upload_id>.json`）再写 D1 `bazaardb_delivery`；server 只最小校验 `snapshot.id` 与 path 一致，不解 base64、不校验图片。BazaarDB puller 通过 bearer-gated `peek` 拿短期预签 URL，成功落地后用 `confirm` 按 DTO 确认；confirm 后 R2 对象 best-effort 删除，D1 保留轻量 ledger 行。

sidecar 表 `bazaardb_snapshot_uploads` 与服务端 `bazaardb_delivery` 的列定义见 schema 参考 / 服务端仓库；本文不重复 DDL。

### 配置与信任

- `BazaarDB / UploadScreenshots`（默认 `false`）：`true` 时所有未上传的 `end_of_run_auto` 截图（含历史）入队；改回 `false` 不删已发送内容，只停止继续上传。
- 服务端 secret `BAZAARDB_PULL_TOKEN`（`wrangler secret put`），仅用于 BazaarDB puller 的 `peek` / `confirm`。
- ingest 端点不鉴权，依赖自定义域访问边界；模组无上传 token。Snapshot DTO 仍是 JSON，`image.encoding = base64`；`image.content_type` 可能是 `image/png`（原图或 PNG derivative）或 `image/jpeg`（JPEG fallback）。服务端 body 上限是 4 MiB，模组上传图片 bytes 上限是 2 MiB。
- 旧的 `bazaarplusplus-installer` BazaarDB 上传链路（含 OS keyring 凭证）已完整删除；旧机器残留 keyring 条目处于 inert 状态。

## 关键文件

- `Game/Screenshots/EndOfRunScreenshotController.cs`、`ScreenshotService.cs`
- `Storage/RunScreenshot/RunScreenshotSqliteStore.cs`、`RunScreenshotRecord.cs`、`RunScreenshotCaptureSource.cs`
- `Game/Screenshots/Upload/BazaarDbSnapshotUploadController.cs`、`BazaarDbSnapshotUploadService.cs`、`BazaarDbSnapshotUploadStore.cs`
- `ModApi/Clients/BazaarDbSnapshotClient.cs`、`ModApi/Models/BazaarDbSnapshotUploadRequest.cs`、`ModApi/ModApiRoutes.cs`
- 服务端（独立仓库 `bazaarplusplus-server`）：`src/features/bazaardb/upload.ts`、`peek.ts`、`confirm.ts`、`migrations/0001_v4_initial.sql`
