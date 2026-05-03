# ModCFServerV3 API Reference

本文档逐接口描述 **请求/响应契约** 和 **服务端处理原理**（D1、R2 的具体读写）。所有路由由 [src/index.ts](../src/index.ts) 注册并分发到 `src/features/v3/` 下的 handler。

整体职责和已知限制见 [ModCFServerV3 README](../README.md)。

## 1. 通用约定

### 1.1 Base URL

生产环境：`https://mod-api-v3.bazaarplusplus.com`（在 [wrangler.toml](../wrangler.toml) 配的 custom domain）。

### 1.2 序列化

- 需要 JSON body 的路由必须带 `Content-Type: application/json`。`http/json.ts` 的 `readJson` 会对其它 content-type 直接抛 `415`。`/health`、`/ghost-battles`、`/logout`、replay-link 和 replay download 路由不解析 JSON body。
- 响应体：`application/json; charset=utf-8`，`/replays/:token` 的 R2 透传响应除外（content-type 由 R2 对象元数据决定，缺省 `application/octet-stream`）。
- 时间戳：所有 `*_at_utc` 字段一律 ISO 8601 UTC（`new Date().toISOString()` 的格式：`2026-04-17T08:30:00.000Z`）。

### 1.3 鉴权

- 鉴权方式：`Authorization: Bearer <token>` 头。token 是 `/activate` 或 `/login` 返回的 32-byte base64url 字符串（43 字符），由 [token/generate.ts](../src/token/generate.ts) 用 `crypto.getRandomValues` 生成。
- 校验流程（[requireBearerAuth.ts](../src/features/v3/requireBearerAuth.ts)）：
  1. 解析 Authorization 头，正则 `^Bearer\s+(\S+)$`。失败 → 401 `invalid_token`。
  2. `SELECT token, player_account_id, revoked_at_utc FROM tokens WHERE token = ?`。
  3. 若行不存在或 `revoked_at_utc != null` → 401 `invalid_token`。
  4. 异步刷新 `last_used_at_utc`（失败静默吞掉，不阻塞主流程）。
  5. 返回 `{ token, playerAccountId }` 给 handler。

Token 没有 `expires_at_utc`，撤销路径只有 `/logout`。详见 README "Known Limitations"。

仓库当前 `wrangler.toml` 仍把 `ALLOW_UNAUTHENTICATED_REPLAY_LINKS` 和 `ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS` 设为 `true`，所以 replay-link / replay download 在当前 early rollout 配置下允许无 bearer 调用。`/ghost-battles` 始终需要 bearer token。

### 1.4 CORS

- preflight：所有 `OPTIONS` 由 [http/cors.ts](../src/http/cors.ts) 的 `preflight` 处理。
- 实际响应：每个成功 / 失败响应都被 `withCors` 包一层，回显 `Origin` 头（缺省 `*`），允许 `GET, POST, OPTIONS`，允许的 headers 见 `cors.ts`。
- `Allow-Origin` 当前回显 origin，没有白名单。详见 README "Known Limitations"。

### 1.5 错误响应

- 统一形态：`{ "error": "<error_code>" }` + 对应 HTTP 状态码。
- 错误码全为 snake_case 字符串。
- handler 内部如果 throw 一个 `Response`（用于错误短路），index.ts 的 catch 会把它当成最终响应；其它异常一律抛回 Workers runtime（500）。

### 1.6 健康检查

- `GET /health` → 200 `{ "ok": true }`。
- 唯一一个不需要 JSON body、不查 D1/R2 的端点。给 LB 和监控用。

---

## 2. 账户与会话

### 2.1 `POST /activate`

注册一个新账户并立即返回 bearer token，供 mod 在不再走 `/login` 的情况下直接使用。

**鉴权**：无。

**请求体**：
```json
{
  "player_account_id": "string, 非空",
  "player_username": "string, 非空",
  "password": "string, 非空（不会被 trim）"
}
```

**成功响应** (200)：
```json
{
  "token": "43-char base64url",
  "player_account_id": "...",
  "player_username": "..."
}
```

**错误码**：

| Status | error | 触发条件 |
|---|---|---|
| 400 | `invalid_request` | 三个字段任一为空 |
| 409 | `player_account_id_taken` | `users.player_account_id` UNIQUE 冲突 |
| 409 | `player_username_taken` | `users.player_username` UNIQUE 冲突 |

**处理原理**（[activate.ts](../src/features/v3/activate.ts)）：

1. 用 [crypto/password.ts](../src/crypto/password.ts) 的 `hashPassword` 把密码哈希为 `v1:<salt-base64>:<digest-base64>`（单轮 salted SHA-256，已知限制）。
2. 用 `generateBearerToken` 生成 token。
3. 把两条 INSERT 放进一个 `env.DB.batch([...])` 里：`users` 和 `tokens`。D1 的 batch 在 SQLite 层是 BEGIN/COMMIT，要么全部成功要么全部回滚。
4. 不再做前置 SELECT 检查；直接依赖 `users` 表的 UNIQUE 约束 + 捕获 `UNIQUE constraint failed: users.<col>` 错误信息映射回 409。这避免了"两个并发请求同时通过检查再 INSERT"的 TOCTOU 缝隙。

### 2.2 `POST /login`

用用户名/密码换取一个 bearer token。

**鉴权**：无。

**请求体**：
```json
{
  "player_username": "string, 非空",
  "password": "string, 非空"
}
```

**成功响应** (200)：与 `/activate` 同形态。

**错误码**：

| Status | error | 触发条件 |
|---|---|---|
| 400 | `invalid_request` | 缺字段 |
| 401 | `invalid_credentials` | 用户不存在 **或** 密码错 |

**处理原理**（[login.ts](../src/features/v3/login.ts)）：

1. `SELECT player_account_id, player_username, password_hash FROM users WHERE player_username = ?`。
2. `verifyPassword`：拿存储 hash 里的 salt 重新 SHA-256，再做**常量时间字节比较**（`mismatch |= a ^ b` 累积）防止时序侧信道。
3. 用户名错和密码错统一返回 `invalid_credentials`，避免暴露用户名是否存在。
4. `env.DB.batch` 里同时插入新 `tokens` 行 + 更新 `users.last_login_at_utc`、`users.updated_at_utc`。

> 多次 `/login` 不会撤销旧 token——同一用户允许有多条活跃 token，每条都靠各自的 `revoked_at_utc` 单独管理生命周期。

### 2.3 `POST /logout`

撤销当前请求所用的 bearer token。

**鉴权**：必须。

**请求体**：无（忽略）。

**成功响应**：`204 No Content`。

**错误码**：401 `invalid_token`（同 `requireBearerAuth`）。

**处理原理**（[logout.ts](../src/features/v3/logout.ts)）：

1. `requireBearerAuth` 解析并校验 token。
2. `UPDATE tokens SET revoked_at_utc = ? WHERE token = ?`，写入当前时间。
3. **幂等**：再次撤销已撤销 token 仍然返回 204（UPDATE 影响 0 行不视为错误）。

> Logout 只撤销当前 token，不清除该用户的其它 token。要"全设备登出"需要 `WHERE player_account_id = ?` 的批量撤销，目前没实现。

---

## 3. 数据上传

### 3.1 `POST /run-bundles`

提交一个完整 run 的归档：原始 artifact（gzip 压缩的 messagepack）放进 R2，结构化投影写进 D1 的 `runs` 和 `battles`。

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
8. **battles 投影过滤**。对每条 battle，只在 `opponent_account_id` 是已注册玩家（存在于 D1 `users.player_account_id` 主键）或 `opponent_account_id` 等于上传者本人时才投影。`loadKnownOpponentAccountIds` 把 distinct opponent id 用一条 `WHERE player_account_id IN (...)` 主键查询取回。
9. **batch upsert**。被选中的 battles 用 `INSERT ... ON CONFLICT(battle_id) DO UPDATE SET ...` 拼成 prepared statements，一次 `env.DB.batch(...)` 提交。

> 性能笔记：covering index `idx_battles_opponent_recorded_covering`（migration 0003，migration 0008 追加 `is_bundle_final_battle`）让 ghost-battle 读路径不再回表。批量 battle 写入时每行多写投影列到该索引，但 D1 的"rows written"计费是按行而非按字节，所以 billing 不受影响。

---

## 4. Ghost Battles 查询

### 4.1 `GET /ghost-battles`

返回 `GHOST_QUERY_LOOKBACK_DAYS` 回溯窗口内别人上传的 run 中、和当前玩家对战过的所有 battle 投影。这就是 mod 内 History Panel 的"幽灵战斗"列表。

**鉴权**：必须。返回的是"`opponent_account_id == 当前 token 的 player_account_id`"的所有 battles。

**Query 参数**：
- `limit`（可选）：1–200，缺省 200，超界自动 clamp。
- 其它 query 参数被忽略（特别是历史上的 `player_account_id`）——身份完全由 bearer token 决定。

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

字段语义：返回的 `player_*` 是**对手 run 视角**下的"player"（即上传者本人），`opponent_*` 是当前 token 持有人。这是 payload 原始视角的直接保留——客户端在渲染时按这一约定还原。`is_bundle_final_battle` 是服务端根据上传 bundle 中最后一条 battle projection 计算的原始事实，不做视角翻转。

**错误码**：401 `invalid_token`。

**处理原理**（[queryGhostBattles.ts](../src/features/v3/queryGhostBattles.ts)）：

1. `requireBearerAuth` 取出 `playerAccountId`。
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

## 5. Replay 链路

Replay 链路是两步式：先 mint token，再凭 token 下载文件。中间引入一个短 TTL 的 `replay_tokens` 表既可以做权限校验，也可以避免直接把长效的 R2 object key 暴露给客户端。

### 5.1 `POST /ghost-battles/:battleId/replay-link`

为某场 battle 申请一个 5 分钟有效的下载 URL。

**鉴权**：代码默认必须。环境变量 `ALLOW_UNAUTHENTICATED_REPLAY_LINKS=true` 可关闭，与 `ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS` 配套使用，是早期 rollout 的兼容开关。仓库当前 `wrangler.toml` 配置为 `true`。

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
| 401 | `invalid_token` | 鉴权打开时 token 缺失/无效 |
| 403 | `replay_forbidden` | 已鉴权但 `battles.opponent_account_id` 不等于当前 token 的 player_account_id；或者匿名模式下 battle 没有 `opponent_account_id` |
| 404 | `battle_not_found` | battle_id 不存在 |

**处理原理**（[createReplayLink.ts](../src/features/v3/createReplayLink.ts)）：

1. 拿到 `requesterPlayerAccountId`：鉴权开则来自 token，匿名模式则置 null。
2. `SELECT battle_id, player_account_id, opponent_account_id FROM battles WHERE battle_id = ?`。
3. **权限规则**：只允许"battle 的 opponent"申请——也就是说，能下载的是"被对手扔上来的、自己作为对手的那场战斗"。这与 ghost-battles 查询的语义一致。
4. `tokenOwnerPlayerAccountId = requesterPlayerAccountId ?? battleRow.opponent_account_id`：匿名模式下 token 归属取自 battle 的 opponent，方便后续下载流程做 owner 比对。
5. 生成 token `replay_<uuid 去掉横线>`，INSERT `replay_tokens` 行：`expires_at_utc = now + 5min`，`used_at_utc = null`，`revoked_at_utc = null`。
6. 用 `new URL("/replays/${token}", request.url)` 拼出绝对 URL 返回。

### 5.2 `GET /replays/:token`

凭 replay token 把对应 run bundle 的 R2 对象**流式**返回。

**鉴权**：代码默认必须。可由 `ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS=true` 临时关闭。仓库当前 `wrangler.toml` 配置为 `true`。

**Path 参数**：`token`，URL-encoded（即 `createReplayLink` 返回的 `replay_<uuid>`）。

**响应**：
- 成功：200 + R2 object 的二进制流；`content-type` 来自 R2 `httpMetadata.contentType`（落库时是 `artifact_codec`）；`content-length` 来自 R2 `object.size`。
- 失败：JSON 错误。

**错误码**：

| Status | error | 触发条件 |
|---|---|---|
| 401 | `invalid_token` | 鉴权开时 bearer 缺失/无效 |
| 403 | `replay_token_forbidden` | 鉴权开且 token 持有者不是当初的请求者 |
| 404 | `replay_token_not_found` | token 不存在或被撤销 |
| 410 | `replay_token_expired` | `expires_at_utc < now` |
| 410 | `artifact_expired` | battle 不存在、或对应 run_bundle 不存在、或 R2 object 已被 lifecycle 清掉 |

**处理原理**（[downloadReplay.ts](../src/features/v3/downloadReplay.ts)）：

1. `SELECT * FROM replay_tokens WHERE token = ?`。失效条件：missing、`revoked_at_utc != null`（404）；`expires_at_utc < now`（410）；`requested_by_player_account_id != requester`（鉴权开时 403）。
2. **解 R2 object key 链**：`replay_tokens.battle_id` → `battles.bundle_id` → `run_bundles.object_key`。任一环节缺失都返回 410 `artifact_expired`，因为没有保留者负责清理 D1 metadata；R2 lifecycle 删除 object 后，metadata 会孤悬。
3. **首次使用记录**。如果 `used_at_utc` 是 null，写入当前时间。这只是记录字段，不会阻止后续重复下载——TTL 内 token 可复用。详见 README "Known Limitations"。
4. **流式响应**：`new Response(object.body, ...)`，直接把 R2 的 ReadableStream 透传给客户端。**不**走 `arrayBuffer()`，避免大 artifact 一次性占用 Workers 内存。`content-length` 主动 set 给客户端做进度计算。

---

## 6. 后端组件职责速查

| 组件 | 角色 | 关键表/桶 |
|---|---|---|
| **D1 (`DB`)** | 关系型 metadata 主存储 | `users`、`tokens`、`run_bundles`、`runs`、`battles`、`replay_tokens` |
| **R2 (`RUN_BUNDLE_BUCKET`)** | 大 artifact 存储 | `run-bundles/<player>/<run>/<hash>.mpack.gz` |

### 数据保留与清理

- D1 行不会自动删除。`run_bundles`、`runs`、`battles` 累加；如需清理需在外部跑 GC。
- R2 对象的清理委托给 **bucket lifecycle 配置**（不在本仓库），`RUN_BUNDLE_RETENTION_DAYS` 只是写到 object metadata 上的提示。
- `replay_tokens` 行不会自动删除，但用 `expires_at_utc` 做被动失效。

### 鉴权身份谱

- `users.player_account_id`：账户的稳定标识，PRIMARY KEY。
- `tokens.player_account_id`：token 归属用户，FK → users。
- `battles.player_account_id`：上传该 battle 的人（行级所有权）。
- `battles.player_account_id_in_payload`：原始 payload 里 player 的 account（语义层信息），就是 ghost-battles 响应里 `player_account_id` 字段的来源。
- `battles.opponent_account_id`：对手账户，**ghost-battle 路由的核心查询键**。
