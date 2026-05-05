# ModCFServerV3 API Reference

本文档逐接口描述 **请求/响应契约** 和 **服务端处理原理**（D1、R2 的具体读写）。所有路由由 [src/index.ts](../src/index.ts) 注册并分发到 `src/features/v3/` 下的 handler。

整体职责和已知限制见 [ModCFServerV3 README](../README.md)。

## 1. 通用约定

### 1.1 Base URL

生产环境：`https://mod-api-v3.bazaarplusplus.com`（在 [wrangler.toml](../wrangler.toml) 配的 custom domain）。

### 1.2 序列化

- 需要 JSON body 的路由必须带 `Content-Type: application/json`。`http/json.ts` 的 `readJson` 会对其它 content-type 直接抛 `415`。`/health`、`/ghost-battles`、replay-link 和 replay download 路由不解析 JSON body。
- 响应体：`application/json; charset=utf-8`，`/replays/:token` 的 R2 透传响应除外（content-type 由 R2 对象元数据决定，缺省 `application/octet-stream`）。
- 时间戳：所有 `*_at_utc` 字段一律 ISO 8601 UTC（`new Date().toISOString()` 的格式：`2026-04-17T08:30:00.000Z`）。

### 1.3 鉴权

worker **不再做任何鉴权**。所有端点都是公开的。各端点的"身份"来源：

- `POST /run-bundles`：`player_account_id` 来自 request body；缺省回退到 `"anonymous-player"`。
- `GET /ghost-battles`：`player_account_id` 来自 query string，必填；缺省 / 全空白返回 400 `invalid_request`。
- `POST /ghost-battles/:battleId/replay-link`：身份取自 `battles.opponent_account_id`（无需调用方提供）。
- `GET /replays/:token`：仅靠短 TTL 的 replay token 控制访问。

Trade-off 见 README "Known Limitations" 第 1 条。

### 1.4 CORS

- preflight：所有 `OPTIONS` 由 [http/cors.ts](../src/http/cors.ts) 的 `preflight` 处理。
- 实际响应：每个成功 / 失败响应都被 `withCors` 包一层，回显 `Origin` 头（缺省 `*`），允许 `GET, POST, OPTIONS`。
- 允许的 request headers：`content-type, x-bpp-timestamp, x-bpp-content-sha256, x-bpp-signature`。
- `Allow-Origin` 当前回显 origin，没有白名单。详见 README "Known Limitations"。

### 1.5 错误响应

- 统一形态：`{ "error": "<error_code>" }` + 对应 HTTP 状态码。
- 错误码全为 snake_case 字符串。
- handler 内部如果 throw 一个 `Response`（用于错误短路），index.ts 的 catch 会把它当成最终响应；其它异常一律抛回 Workers runtime（500）。

### 1.6 健康检查

- `GET /health` → 200 `{ "ok": true }`。
- 唯一一个不查 D1/R2 的端点。给 LB 和监控用。

---

## 2. 数据上传

### 2.1 `POST /run-bundles`

提交一个完整 run 的归档：原始 artifact（gzip 压缩的 messagepack）放进 R2，结构化投影写进 D1 的 `runs` 和 `battles`，并把上传者本人 upsert 进 `seen_player_accounts` 注册表。

**鉴权**：无。任何能访问到 worker 的客户端都能提交。这是数据收集阶段的有意设计，详见 README "Known Limitations"。

**请求体**：
```json
{
  "schema_version": 3,
  "player_account_id": "string | null  // 缺省视为 'anonymous-player'",
  "submitted_at_utc": "ISO-8601 UTC",
  "artifact_codec": "string  // 通常 application/x-bpp-runbundle+msgpack+gzip",
  "artifact_bytes": "string (base64)  // 也接受 number[]，详见下文",
  "run_projection": {
    "run_id": "string, 非空",
    "status": "string, 非空",
    "ended_at_utc": "ISO-8601 UTC, 非空",
    "hero_id": "string|null", "hero_name": "string|null",
    "player_rank": "string|null", "player_rating": "number|null", "player_position": "number|null",
    "started_at_utc": "string|null",
    "final_day": "number|null", "final_wins": "number|null", "final_losses": "number|null",
    "final_player_rank": "string|null", "final_player_rating": "number|null", "final_player_position": "number|null"
  },
  "battle_projections": [
    {
      "battle_id": "string, 非空",
      "run_id": "string  // 必须等于 run_projection.run_id",
      "recorded_at_utc": "ISO-8601 UTC",
      "day": "number|null",
      "player_name": "string|null", "player_account_id": "string|null", "player_hero": "string|null",
      "player_rank": "string|null", "player_rating": "number|null", "player_level": "number|null",
      "opponent_name": "string|null", "opponent_account_id": "string|null", "opponent_hero": "string|null",
      "opponent_rank": "string|null", "opponent_rating": "number|null", "opponent_level": "number|null",
      "result": "string|null",
      "replay_available": "boolean"
    }
  ]
}
```

**成功响应** (200)：
```json
{
  "status": "accepted",
  "bundle_id": "<run_id>  // 重传同一 run 时复用同一个 bundle_id",
  "object_key": "run-bundles/<player>/<run>/<hash>.mpack.gz"
}
```

**错误码**：

| Status | error | 触发条件 |
|---|---|---|
| 400 | `invalid_run_bundle_request` | schema/run/artifact 必填项缺失或路径段含非法字符 |
| 400 | `battle_id_required` | 某个 battle 缺 `battle_id` |
| 400 | `battle_run_id_mismatch` | battle 的 `run_id` 与 `run_projection.run_id` 不一致 |
| 400 | `too_many_opponent_account_ids` | 单次上传中 distinct `opponent_account_id` 超过 20 个 |
| 415 | (text body) | content-type 不是 application/json |

**处理原理**（[uploadRunBundle.ts](../src/features/v3/uploadRunBundle.ts)）：

1. **解码 artifact_bytes**。`decodeArtifactBytes` 同时接受：
   - `string`：当作 base64 解码（标准 base64，必须带 padding）。新版 mod 用这条路径。
   - `number[]`：旧版 mod 兼容路径，逐元素校验 0–255。
   每次成功解码都打日志 `upload_run_bundle.artifact_bytes_received` 带 `encoding=base64|byte-array`，用于在 Cloudflare 日志里跟踪迁移占比。
2. **校验 battle 投影边界**。每条 battle 必须有 `battle_id`，且 `battle.run_id == run_projection.run_id`，避免一次上传把别的 run 的 battles 串进来；单次上传最多接受 20 个 distinct `opponent_account_id`，防止未鉴权上传放大后续 D1 查询成本。
3. **路径段清洗**。`player_account_id` 和 `run_id` 必须匹配 `^[A-Za-z0-9._-]{1,128}$`，否则 400。`payloadHash` 是 sha256 base64，会再转一次 base64url（`+/=` → `-_`、去 padding）以保证 R2 key 始终安全。
4. **R2 PUT**。Object key 形如 `run-bundles/<player>/<run>/<payloadHashUrlSafe>.mpack.gz`，写 `customMetadata.retention_days`（值来自 `RUN_BUNDLE_RETENTION_DAYS` 环境变量，当前 5 天，由 R2 lifecycle 配置在外部清理）。
5. **idempotency**。`INSERT OR REPLACE INTO run_bundles` 使用 `run_id` 作为确定性 `bundle_id`，同一 run 的重传会覆盖 metadata；R2 上同 key 直接覆盖（同一 hash 也是同一内容）。这让 mod 可以无脑重传。
6. **runs upsert**。`INSERT ... ON CONFLICT(run_id) DO UPDATE SET ...`：每次上传都按 `run_id` upsert，让最新一次上传的 run projection 覆盖之前的。
7. **bundle-final 标记**。服务端把 `battle_projections` 中最后一条 battle 的 `battle_id` 记为 `finalBattleId`；该 battle 被投影时写入 `is_bundle_final_battle = 1`。
8. **battles 投影过滤**。对每条 battle，只在 `opponent_account_id` 是已注册玩家（存在于 D1 `seen_player_accounts.player_account_id` 主键）或 `opponent_account_id` 等于上传者本人时才投影。`loadKnownOpponentAccountIds` 把 distinct opponent id 用一条 `WHERE player_account_id IN (...)` 主键查询取回。
9. **batch upsert**。被选中的 battles 用 `INSERT ... ON CONFLICT(battle_id) DO UPDATE SET ...` 拼成 prepared statements，一次 `env.DB.batch(...)` 提交。
10. **uploader 自举到 seen_player_accounts**。`rememberUploader(env, persistedPlayerAccountId, createdAtUtc)` 在最后一次写完后跑：若 `player_account_id` 不是 `anonymous-player`，做 `INSERT ... ON CONFLICT(player_account_id) DO UPDATE SET last_seen_at_utc = excluded.last_seen_at_utc`。这就是把"上传过 run 的玩家"自动加进 ghost-battles 对手过滤白名单的机制。

> 性能笔记：covering index `idx_battles_opponent_recorded_covering`（migration 0003，migration 0008 追加 `is_bundle_final_battle`）让 ghost-battle 读路径不再回表。批量 battle 写入时每行多写投影列到该索引，但 D1 的"rows written"计费是按行而非按字节，所以 billing 不受影响。

---

## 3. Ghost Battles 查询

### 3.1 `GET /ghost-battles`

返回 `GHOST_QUERY_LOOKBACK_DAYS` 回溯窗口内别人上传的 run 中、和指定玩家对战过的所有 battle 投影。这就是 mod 内 History Panel 的"幽灵战斗"列表。

**鉴权**：无。身份完全由 `player_account_id` query 参数提供。

**Query 参数**：

| 名称 | 必填 | 说明 |
|---|---|---|
| `player_account_id` | 是 | 要查询的玩家账户 ID。空字符串 / 全空白 → 400 `invalid_request`。 |
| `limit` | 否 | 1–200，缺省 200，超界自动 clamp |

**成功响应** (200)：
```json
{
  "battles": [
    {
      "battle_id": "...",
      "recorded_at_utc": "...",
      "day": 5,
      "player_name": "对手提交时记录的玩家名",
      "player_account_id": "对手提交时记录的玩家 account id（payload 列）",
      "player_hero": "...", "player_rank": "...", "player_rating": 100, "player_level": 8,
      "opponent_name": "...", "opponent_account_id": "...",
      "opponent_hero": "...", "opponent_rank": "...", "opponent_rating": 100, "opponent_level": 8,
      "result": "win|lose|...",
      "is_bundle_final_battle": false,
      "replay": { "available": true }
    }
  ]
}
```

字段语义：返回的 `player_*` 是**对手 run 视角**下的"player"（即上传者本人），`opponent_*` 是当前查询的目标账号。这是 payload 原始视角的直接保留——客户端在渲染时按这一约定还原。`is_bundle_final_battle` 是服务端根据上传 bundle 中最后一条 battle projection 计算的原始事实，不做视角翻转。

**错误码**：

| Status | error | 触发条件 |
|---|---|---|
| 400 | `invalid_request` | `player_account_id` query 参数缺失或仅含空白 |

**处理原理**（[queryGhostBattles.ts](../src/features/v3/queryGhostBattles.ts)）：

1. 用 `trimString` 读 `url.searchParams.get("player_account_id")`；空 → 400。
2. lookback window：`fromUtc = now - GHOST_QUERY_LOOKBACK_DAYS * 86400000`。
3. SQL：
   ```sql
   SELECT 18 列 FROM battles
   WHERE opponent_account_id = ?
     AND recorded_at_utc >= ?
   ORDER BY recorded_at_utc DESC, battle_id DESC
   LIMIT ?
   ```
4. 这条 SQL 完全命中 `idx_battles_opponent_recorded_covering`（opponent_account_id, recorded_at_utc DESC, battle_id DESC + 全部投影列，包含 `is_bundle_final_battle`）。**WHERE 用前两列定位、ORDER 用三列排序、SELECT 全部从索引页拿到**，零回表。所以单次查询的 D1 "rows read" 计费 = 实际返回行数（≤200），不会被 widening cost 翻倍。
5. 字段重命名：响应里的 `player_account_id` 来自 `battles.player_account_id_in_payload`，因为 `battles.player_account_id` 是上传者归属字段（用来做行级所有权追踪），和 payload 里的"这条战斗里 player 的 account"语义不同。

---

## 4. Replay 链路

Replay 链路是两步式：先 mint token，再凭 token 下载文件。中间引入一个短 TTL 的 `replay_tokens` 表既可以做权限校验，也可以避免直接把长效的 R2 object key 暴露给客户端。

### 4.1 `POST /ghost-battles/:battleId/replay-link`

为某场 battle 申请一个 5 分钟有效的下载 URL。

**鉴权**：无。

**Path 参数**：`battleId`，URL-encoded。

**请求体**：无。

**成功响应** (200)：
```json
{
  "download_url": "https://<host>/replays/replay_<uuid>",
  "expires_at_utc": "ISO-8601, 5 分钟后"
}
```

**错误码**：

| Status | error | 触发条件 |
|---|---|---|
| 403 | `replay_forbidden` | battle 没有 `opponent_account_id`（无法决定 token 持有者） |
| 404 | `battle_not_found` | battle_id 不存在 |

**处理原理**（[createReplayLink.ts](../src/features/v3/createReplayLink.ts)）：

1. `SELECT battle_id, player_account_id, opponent_account_id FROM battles WHERE battle_id = ?`。
2. 如果 `opponent_account_id` 为空 → 403。
3. `tokenOwnerPlayerAccountId = battleRow.opponent_account_id`：用于 `replay_tokens.requested_by_player_account_id` 写入字段（信息性，不再做访问控制）。
4. 生成 token `replay_<uuid 去掉横线>`，INSERT `replay_tokens` 行：`expires_at_utc = now + 5min`，`used_at_utc = null`，`revoked_at_utc = null`。
5. 用 `new URL("/replays/${token}", request.url)` 拼出绝对 URL 返回。

### 4.2 `GET /replays/:token`

凭 replay token 把对应 run bundle 的 R2 对象**流式**返回。

**鉴权**：无。

**Path 参数**：`token`，URL-encoded（即 `createReplayLink` 返回的 `replay_<uuid>`）。

**响应**：
- 成功：200 + R2 object 的二进制流；`content-type` 来自 R2 `httpMetadata.contentType`（落库时是 `artifact_codec`）；`content-length` 来自 R2 `object.size`。
- 失败：JSON 错误。

**错误码**：

| Status | error | 触发条件 |
|---|---|---|
| 404 | `replay_token_not_found` | token 不存在或被撤销 |
| 404 | `battle_not_found` | replay token 指向的 battle 不存在 |
| 410 | `replay_token_expired` | `expires_at_utc < now` |
| 410 | `artifact_expired` | run_bundle 不存在或 R2 object 已被 lifecycle 清掉 |

**处理原理**（[downloadReplay.ts](../src/features/v3/downloadReplay.ts)）：

1. `SELECT * FROM replay_tokens WHERE token = ?`。失效条件：missing、`revoked_at_utc != null`（404）；`expires_at_utc < now`（410）。
2. **解 R2 object key 链**：`replay_tokens.battle_id` → `battles.bundle_id` → `run_bundles.object_key`。任一环节缺失都返回 410 `artifact_expired`，因为没有保留者负责清理 D1 metadata；R2 lifecycle 删除 object 后，metadata 会孤悬。
3. **首次使用记录**。如果 `used_at_utc` 是 null，写入当前时间。这只是记录字段，不会阻止后续重复下载——TTL 内 token 可复用。详见 README "Known Limitations"。
4. **流式响应**：`new Response(object.body, ...)`，直接把 R2 的 ReadableStream 透传给客户端。**不**走 `arrayBuffer()`，避免大 artifact 一次性占用 Workers 内存。`content-length` 主动 set 给客户端做进度计算。

---

## 5. 后端组件职责速查

| 组件 | 角色 | 关键表/桶 |
|---|---|---|
| **D1 (`DB`)** | 关系型 metadata 主存储 | `seen_player_accounts`、`run_bundles`、`runs`、`battles`、`replay_tokens` |
| **R2 (`RUN_BUNDLE_BUCKET`)** | 大 artifact 存储 | `run-bundles/<player>/<run>/<hash>.mpack.gz` |

### 数据保留与清理

- D1 行不会自动删除。`run_bundles`、`runs`、`battles`、`seen_player_accounts` 累加；如需清理需在外部跑 GC。
- R2 对象的清理委托给 **bucket lifecycle 配置**（不在本仓库），`RUN_BUNDLE_RETENTION_DAYS` 只是写到 object metadata 上的提示。
- `replay_tokens` 行不会自动删除，但用 `expires_at_utc` 做被动失效。

### 身份字段速查

- `seen_player_accounts.player_account_id`：上传过 run 的玩家集合，PRIMARY KEY，作为 `/run-bundles` opponent 过滤的白名单。
- `battles.player_account_id`：上传该 battle 的人（行级所有权，与 `run_bundles.player_account_id` 同源）。
- `battles.player_account_id_in_payload`：原始 payload 里 player 的 account（语义层信息），就是 ghost-battles 响应里 `player_account_id` 字段的来源。
- `battles.opponent_account_id`：对手账户，**ghost-battle 路由的核心查询键**。
