# BazaarDB Screenshot Upload

## Scope

终局自动截图（end-of-run screenshot）由模组直接上传到 `bazaarplusplus-server`（V4 mod backend，部署 `mod-api-v4.bazaarplusplus.com`），BazaarDB 再从服务端按天拉取——模组与 BazaarDB 之间不再有直接连接。

- 默认关闭，由用户在 Bazaar++ 设置坞中开启 `Upload screenshots to BazaarDB` 才会启动后台上传任务。
- 截图 PNG 字节（由 `ScreenshotService.WriteCurrentFrameToFile` 用 Unity `EncodeToPNG` 生成）以 base64 直接放在 JSON 请求体里，复用现有 V3 JSON 上传管线，不引入 multipart。
- 上传链路依然不附加任何鉴权头；BazaarDB 拉取端点用 Bearer token 保护。
- 翻开开关时已存在的历史截图也会被回填上传——回填策略是「`run_screenshots` 里 `capture_source = 'end_of_run_auto'` 且 sidecar 表里还没出现的行，统统入队」，无时间戳 cutoff。

## Client Flow

1. 终局截图照旧由 `EndOfRunScreenshotController` 抓帧、写 PNG、写 `run_screenshots` 行——这条链路与是否开启 BazaarDB 上传无关。
2. `BazaarDbScreenshotUploadController` 每个 tick（20s 启动延迟 + 180s 间隔，复用 `StartupUploadAttemptRunner`）触发一次上传循环。开关关闭时在 tick 顶部短路，零数据库读、零网络。
3. 每次循环先调 `BazaarDbScreenshotUploadStore.EnsureBackfilled`，用 `INSERT OR IGNORE … WHERE NOT EXISTS …` 把缺失的 sidecar 行补齐。
4. 取最多 3 条 `status = 'pending'` 的截图，按 `captured_at_utc ASC` 排序。
5. 对每条截图：读盘 PNG → 装载玩家身份（`BppClientCacheBridge.TryGetProfileAccountId`；V4 后若取不到 id 则**整轮短路跳过**,不再发 `"anonymous-player"` sentinel）→ 构造 `BazaarDbScreenshotUploadRequest` → 调 `BazaarDbScreenshotClient.UploadScreenshotAsync` → 按结果落库。
6. 结果分类：
   - `Success` → `MarkUploaded(uploaded_at_utc)`，sidecar status `uploaded`。
   - 4xx 除 408 / 429 → `MarkPermanentFailure(error)`，sidecar status `permanent_failure`，不再重试。
   - 5xx / 网络 / 408 / 429 → `MarkTransientFailure(error)`，sidecar status 留在 `pending`，`attempts++`，下个 tick 继续尝试。
   - 截图文件已被删除或行不可读 → `MarkPermanentFailure("build_snapshot_failed")`。
7. RunLifecycle 事件触发：run 退出时 `_startupGate.ArmImmediateAttempt`，下一帧立刻上传。
8. 设置开关 false→true：`OnEnabledChanged(true)` 同样调用 `ArmImmediateAttempt`，无需等下一轮 180s。

## Server Flow

V4 `bazaarplusplus-server` 暴露两个端点（V3 时代的 `/bazaardb/image/{id}` Worker 代理已废除,改为公开桶直供）：

| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | `/bazaardb-screenshots` | open (无鉴权) | 模组上传 |
| GET | `/bazaardb/manifest?date=YYYY-MM-DD&cursor=&limit=` | `Authorization: Bearer <BAZAARDB_PULL_TOKEN>` | BazaarDB 游标分页拉当日截图列表，行内带公开 `image_url` |

Ingest 写顺序：先 R2 `BAZAARDB_BUCKET.put(bazaardb/{YYYY-MM-DD}/{screenshot_id}.png)`（V4 key 去掉了 V3 的 `screenshots/` 前缀段），再 D1 `bazaardb_screenshots` 单行 `INSERT … ON CONFLICT(screenshot_id) DO UPDATE SET …`。R2 成功而 D1 失败时 best-effort `R2.delete` 清孤儿后返回 500；下次同 `screenshot_id` 重传会覆盖同一确定性 key，不会累积重复。

Manifest 查询走 `idx_bazaardb_screenshots_cursor(captured_date_utc, uploaded_at_utc, screenshot_id)` 复合游标分页（row-value comparison `(uploaded_at_utc, screenshot_id) > (?, ?)`,因为 `screenshot_id` 是高熵 GUID 非单调,不能单独作游标）。Manifest 响应每行带 `image_url = https://bazaardb-assets-v4.bazaarplusplus.com/{r2_key}`,BazaarDB 拿到 URL 直接走公开桶下载,**不再经过 Worker proxy**。Manifest 端点的 bearer 鉴权仍保留(行里包含 `player_account_id`/name/rank/rating 等可识别信息,不能公开 enumerable)。

## Sidecar Schema

新加在 `RunLogSqliteSchema.BootstrapSql`（schema version 12 → 13）：

```sql
CREATE TABLE IF NOT EXISTS bazaardb_screenshot_uploads (
    screenshot_id          TEXT PRIMARY KEY,
    status                 TEXT NOT NULL,             -- 'pending' | 'uploaded' | 'permanent_failure'
    attempts               INTEGER NOT NULL DEFAULT 0,
    last_attempted_at_utc  TEXT NULL,
    last_error             TEXT NULL,
    uploaded_at_utc        TEXT NULL,
    FOREIGN KEY (screenshot_id) REFERENCES run_screenshots(screenshot_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_bazaardb_screenshot_uploads_status
    ON bazaardb_screenshot_uploads(status);
```

CASCADE 保证 `run_screenshots` 行被删时 sidecar 行也清掉。

## D1 Schema (服务端)

```sql
CREATE TABLE bazaardb_screenshots (
  screenshot_id     TEXT PRIMARY KEY,
  player_account_id TEXT NOT NULL,
  run_id            TEXT,
  hero_name         TEXT,
  final_days        INTEGER,
  final_victories   INTEGER,
  player_name       TEXT,
  player_rank       TEXT,
  player_rating     INTEGER,
  player_position   INTEGER,
  captured_at_utc   TEXT NOT NULL,
  captured_date_utc TEXT NOT NULL,                    -- YYYY-MM-DD 派生自 captured_at_utc, 用于 manifest 索引
  image_format      TEXT NOT NULL,                    -- 当前固定 'png'
  image_sha256      TEXT NOT NULL,
  image_bytes       INTEGER NOT NULL,
  r2_key            TEXT NOT NULL,
  uploaded_at_utc   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
  schema_version   INTEGER NOT NULL
);
CREATE INDEX idx_bazaardb_screenshots_date
  ON bazaardb_screenshots(captured_date_utc, uploaded_at_utc);
```

Migration: V4 已经把所有 schema 合进 `bazaarplusplus-server/migrations/0001_v4_initial.sql`(`bazaardb_screenshots` 表 + `idx_bazaardb_screenshots_cursor` 索引)。

## 当前实现文件

模组侧：

- `Core/Config/BppConfig.cs` (`BazaarDbUploadEnabled` 配置项)
- `Game/Settings/BppSettingsDockCatalog.cs` (设置坞条目 + 切换桥接)
- `Game/Screenshots/Upload/BazaarDbScreenshotUploadController.cs`
- `Game/Screenshots/Upload/BazaarDbScreenshotUploadService.cs`
- `Game/Screenshots/Upload/BazaarDbScreenshotUploadStore.cs`
- `Game/Screenshots/Upload/BazaarDbScreenshotUploadSnapshot.cs`
- `Game/Screenshots/Upload/BazaarDbScreenshotUploadSettingsMenuLabel.cs`
- `ModApi/Clients/BazaarDbScreenshotClient.cs`
- `ModApi/Models/BazaarDbScreenshotUploadRequest.cs`
- `ModApi/ModApiRoutes.cs` (`UploadBazaarDbScreenshot`, `BazaarDbManifestBase` 属性)

服务端(独立仓库 `bazaarplusplus-server`)：

- `src/features/bazaardb/upload.ts`
- `src/features/bazaardb/manifest.ts`
- `migrations/0001_v4_initial.sql`(`bazaardb_screenshots` 表)
- `wrangler.toml`(`BAZAARDB_BUCKET` R2 绑定 + 公开桶 custom domain `bazaardb-assets-v4.bazaarplusplus.com`)

## 配置

| Section / Key | 含义 |
|---|---|
| `BazaarDB / UploadScreenshots` | 是否启用 BazaarDB 截图上传，默认 `false`。`true` 时所有未上传的 `end_of_run_auto` 截图都会进入上传队列，包括过去保存的；改回 `false` 时已发送的内容不会被删除，只是停止继续上传 |

服务端 secret（不入仓）：

- `BAZAARDB_PULL_TOKEN` — BazaarDB 拉取端点使用的 Bearer token，通过 `wrangler secret put BAZAARDB_PULL_TOKEN` 设置。

## Notes

- 当前图片格式固定 PNG。Unity `EncodeToPNG` 生成约 1–3 MB 一张（1080p）。
- 服务端 ingest 端点未鉴权，依赖于自定义域 `mod-api-v4.bazaarplusplus.com` 的访问边界；模组没有上传 token。
- 服务端 ingest 校验：`schema_version === 1` / 必需字段非空 / `screenshot_id` 匹配 `/^[A-Za-z0-9._-]{1,128}$/` / `image_format === "png"` / `captured_at_utc` 可解析且不超过当前 UTC 时间 24 小时（为了容忍客户端时钟漂移）/ PNG magic bytes 校验 / 解码后字节数 ≤ 2 MiB（防止 unauthenticated ingest 被滥用）。超过 2 MiB 直接 400 `image_too_large`，不写 R2，不写 D1。
- 若 R2 写入成功但 D1 upsert 失败：服务端在 catch 分支尽力 `BAZAARDB_BUCKET.delete(r2_key)`（best-effort，记 `r2_cleanup_failed` warning）后返回 500；模组下次 tick 会重试。
- BazaarDB 端 cron 时间表与他们自己的拉取语义不在本仓库的范围内；只要保证 `BAZAARDB_PULL_TOKEN` 一致即可对接。
- 已废弃的 `bazaarplusplus-installer` BazaarDB 上传链路已被完整删除（包括 OS keyring 凭证条目）；旧用户机器上的 keyring 残留条目处于 inert 状态，不再被任何代码消费。
- 设计与实现细节见 `docs/superpowers/specs/2026-05-24-bazaardb-upload-in-mod-design.md` 和 `docs/superpowers/plans/2026-05-24-bazaardb-upload-in-mod.md`。
