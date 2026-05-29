# End-of-Run Screenshots & BazaarDB Upload

一条 `end_of_run_auto` 截图管线：终局自动抓图（采集） → 可选地上传到 `bazaarplusplus-server` 供 BazaarDB 拉取（消费）。采集始终发生；上传是默认关闭的附加 feature（`BazaarDB / UploadScreenshots`），两者通过 `run_screenshots` 表解耦。

SQLite 列定义（`run_screenshots`、`bazaardb_screenshot_uploads`）统一见 [sqlite-schema-reference.md](../reference/sqlite-schema-reference.md)，本文不重复。

## 采集（Capture）

当前只保留一种来源 `end_of_run_auto`——只在终局页面自动保存一张主图。旧的 `F9` 手动截图、设置面板 `CAM` 按钮、PVP 战斗截图均已移除。

### Trigger

终局界面出现后，`EndOfRunScreenController` 上挂一个全屏透明鼠标 blocker。满足三条件才触发截图：(1) 终局界面已出现满 10 秒；(2) 本局还没成功过一次终局自动截图；(3) 用户触发了一次合法 `Continue`。10 秒窗口内 blocker 吞掉鼠标点击；窗口过后第一次合法点击会先抓图再放行原始 `Continue`，抓图失败则本局仍可再次尝试。

### Storage

- 文件：`<GameRoot>/BazaarPlusPlusV4/Screenshots/YYYY-MM-DD/<...>_final_run-<runId>.png`
- 元数据：`bazaarplusplus.db` 的 `run_screenshots` 表；运行时只写 `capture_source = end_of_run_auto`、`is_primary = 1`（`battle_id` 等通用列保留但终局截图不写 battle 维度）。

### Runtime Data Sources

抓图排队当下读取：`run_id`（controller 缓存 → `RunContext.CurrentServerRunId`）、`hero_name`、`player_rank`/`player_rating`（`RunLoggingGameDataReader.TryGetPlayerRankSnapshot`）、`player_position`（`BppClientCacheBridge.TryGetPlayerLeaderboardPosition`）、`day`（`Data.Run.Day`）、`victories_at_capture`（`Data.Run.Victories`）。

### UI Suppression

抓图时临时隐藏 Bazaar++ 自己的浮层（设置坞、combat status bar），避免拍进终局图。

### External Reader Contract

外部读取方拿本局主图：查询 `run_id = ?` 且 `is_primary = 1`，图片绝对路径 = `<GameRoot>/BazaarPlusPlusV4/Screenshots/` 拼接行内 `image_relative_path`。

## BazaarDB 上传（可选，默认关闭）

开启 `BazaarDB / UploadScreenshots` 后，`BazaarDbScreenshotUploadController` 在**非 live run** 时按 20s 启动延迟 + 180s 间隔扫描待上传截图，把同一批 `end_of_run_auto` 截图推到 `bazaarplusplus-server`，BazaarDB 再从服务端按天拉取（模组与 BazaarDB 之间无直接连接）。翻开开关会回填历史截图——sidecar 表里缺失的 `end_of_run_auto` 行统统入队，无时间戳 cutoff。

### Client Flow

1. 每个 tick 先 `EnsureBackfilled`（`INSERT OR IGNORE` 补齐 sidecar 行），开关关闭时 tick 顶部短路、零 DB / 零网络。
2. 取最多 3 条 `status='pending'`（按 `captured_at_utc ASC`）。
3. 每条：读盘 PNG → 取 `player_account_id`（`BppClientCacheBridge.TryGetProfileAccountId`，取不到则**整轮短路跳过**，不发占位符）→ `POST /bazaardb-screenshots` → 按结果落库。
4. 结果分类：`Success`→`uploaded`；4xx（除 408/429）→`permanent_failure` 不再重试；5xx / 网络 / 408 / 429 → 留 `pending`、`attempts++` 下轮再试；文件已删 / 行不可读 → `permanent_failure`。
5. run 退出或开关 false→true 时 `ArmImmediateAttempt`，下一帧立即上传，无需等 180s。

### Server Flow

| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | `/bazaardb-screenshots` | open | 模组上传 |
| GET | `/bazaardb/manifest?date=&cursor=&limit=` | `Bearer <BAZAARDB_PULL_TOKEN>` | BazaarDB 游标分页拉当日列表，行内带公开 `image_url` |

Ingest 先写 R2（`bazaardb/{date}/{screenshot_id}.png`）再写 D1 `bazaardb_screenshots`；R2 成功 D1 失败时 best-effort 删孤儿后返回 500，重传覆盖同一确定性 key。Manifest 行带 `image_url = https://bazaardb-assets-v4.bazaarplusplus.com/{r2_key}`，BazaarDB 直接走**公开桶**下载，不经 Worker proxy；manifest 端点仍需 bearer（行含 `player_account_id`/name/rank 等可识别信息，不能公开 enumerable）。服务端 ingest 校验包括 `schema_version===1`、PNG magic bytes、解码后 ≤ 2 MiB（超出直接 400 `image_too_large`）。

sidecar 表 `bazaardb_screenshot_uploads`（本地 schema v13 引入）与 D1 `bazaardb_screenshots` 的列定义见 schema 参考 / 服务端仓库；本文不重复 DDL。

### 配置与信任

- `BazaarDB / UploadScreenshots`（默认 `false`）：`true` 时所有未上传的 `end_of_run_auto` 截图（含历史）入队；改回 `false` 不删已发送内容，只停止继续上传。
- 服务端 secret `BAZAARDB_PULL_TOKEN`（`wrangler secret put`）。
- ingest 端点不鉴权，依赖自定义域访问边界；模组无上传 token。图片格式固定 PNG（Unity `EncodeToPNG`，1080p 约 1–3 MB）。
- 旧的 `bazaarplusplus-installer` BazaarDB 上传链路（含 OS keyring 凭证）已完整删除；旧机器残留 keyring 条目处于 inert 状态。

## 关键文件

- `Game/Screenshots/EndOfRunScreenshotController.cs`、`ScreenshotService.cs`
- `Storage/RunScreenshot/RunScreenshotSqliteStore.cs`、`RunScreenshotRecord.cs`、`RunScreenshotCaptureSource.cs`
- `Game/Screenshots/Upload/BazaarDbScreenshotUploadController.cs`、`BazaarDbScreenshotUploadService.cs`、`BazaarDbScreenshotUploadStore.cs`
- `ModApi/Clients/BazaarDbScreenshotClient.cs`、`ModApi/Models/BazaarDbScreenshotUploadRequest.cs`、`ModApi/ModApiRoutes.cs`
- 服务端（独立仓库 `bazaarplusplus-server`）：`src/features/bazaardb/upload.ts`、`manifest.ts`、`migrations/0001_v4_initial.sql`
