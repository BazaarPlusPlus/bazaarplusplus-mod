# ModCFServerV3

Cloudflare Worker backend for the BazaarPlusPlus mod's V3 online flow.

收 mod 上传的 run bundle，把 artifact 落到 R2、把结构化投影写进 D1，并对外提供 ghost battle 查询和短时效的 replay 下载链接。

## Stack

- **Cloudflare Workers** (TypeScript, ES Modules)
- **D1** for relational metadata
- **R2** for run bundle artifacts

`wrangler.toml` 中的 bindings：

| Binding | 类型 | 用途 |
| --- | --- | --- |
| `DB` | D1 database | 账户、token、runs、battles、replay tokens |
| `RUN_BUNDLE_BUCKET` | R2 bucket | run bundle artifact |

## HTTP Surface

完整的请求/响应契约和服务端处理细节见 [docs/api-reference.md](docs/api-reference.md)。

| Method | Path | 说明 |
| --- | --- | --- |
| `GET` | `/health` | 健康检查 |
| `POST` | `/activate` | 注册账户并签发 bearer token |
| `POST` | `/login` | 用用户名/密码换取 bearer token |
| `POST` | `/logout` | 撤销当前 bearer token |
| `POST` | `/run-bundles` | 上传 run bundle artifact 并投影到 D1（**当前不鉴权**） |
| `GET` | `/ghost-battles` | 查询当前玩家作为 opponent 出现的 battle 列表 |
| `POST` | `/ghost-battles/:battleId/replay-link` | 申请 5 分钟有效的 replay 下载 URL |
| `GET` | `/replays/:token` | 凭 replay token 流式下载 R2 artifact |

路由注册在 [src/index.ts](src/index.ts)，handler 都在 [src/features/v3/](src/features/v3)。

## Migrations

D1 schema 由 `migrations/` 下的有序 SQL 维护，按文件名顺序应用：

| 文件 | 内容 |
| --- | --- |
| `0001_initial_schema.sql` | 初版表结构（users、installation 链路、run_bundles、runs、battles、replay_tokens） |
| `0002_auth_simplification.sql` | 新增 `tokens`，移除 installation 链路，鉴权回退到 username + password + bearer token |
| `0003_ghost_battles_covering_index.sql` | 为 `/ghost-battles` 查询建立 17 列 covering index |
| `0004_run_bundles_submitted_at_index.sql` | 为按 `submitted_at_utc` 同步 run bundle 的分析任务建索引 |
| `0005_runs_ended_at_index.sql` | 为按 `ended_at_utc` 同步 runs 的分析任务建索引 |
| `0006_run_bundles_created_at_index.sql` | 为按服务端 `created_at_utc` 同步 run bundle 建索引 |
| `0007_runs_updated_at_index.sql` | 为按服务端 `updated_at_utc` 同步 runs 建索引 |
| `0008_battles_bundle_final_flag.sql` | 为 ghost battle UI 增加 `is_bundle_final_battle` 并重建 covering index |

## Known Limitations

以下行为是当前 rollout 阶段的有意取舍。在面向更广用户群之前需要重新评估。

- **Bearer token 不会过期。** `tokens` 表没有 `expires_at_utc`。token 一旦由 `/activate` 或 `/login` 签发，就持续有效直到用户主动 `/logout`（写入 `revoked_at_utc`）。没有时间过期、没有空闲超时清理、没有轮换。泄露的 token 永久有效。
- **Bearer token 明文存储。** `tokens.token` 作为主键直接被 `WHERE token = ?` 匹配。一次 D1 dump 等同于所有活跃用户的会话被劫持。
- **密码哈希是单轮 salted SHA-256。** 在用户基数小、流量可信时可接受；持有高价值凭证后不可接受。哈希格式以 `v1:` 命名空间标记，未来可在不破坏旧登录的前提下引入 PBKDF2/scrypt/Argon2。
- **`/run-bundles` 不鉴权。** 任何能访问 worker 的客户端都能为任意 `player_account_id` 提交 bundle。这是数据收集阶段的有意设计。R2 object key 中的路径段以 `[A-Za-z0-9._-]{1,128}` 校验防止 prefix escape，但数据本身是 trust-on-submit。
- **Replay link / download 在当前部署配置下可无鉴权调用。** `wrangler.toml` 仍把 `ALLOW_UNAUTHENTICATED_REPLAY_LINKS` 和 `ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS` 设为 `true`，用于 early rollout 兼容；收紧时两者应成对改为 `false`。
- **Replay token 在 TTL 内可复用。** `replay_tokens.used_at_utc` 在首次下载时记录，但不阻止 5 分钟窗口内的后续下载。捕获的 replay URL 在窗口内可重放。改成严格一次性的方式是把首次使用记录换成 `UPDATE … SET used_at_utc = ? WHERE token = ? AND used_at_utc IS NULL` 并要求 affected-rows == 1。
- **`artifact_bytes` 同时接受 base64 字符串和 JSON byte array。** 旧版 mod 走数组，新版 mod 走 base64（线上体积约 1/3）。服务端在每次上传时打日志 `upload_run_bundle.artifact_bytes_received` 带 `encoding=base64|byte-array`，可在 Cloudflare 日志里跟踪迁移占比。当 byte-array 占比归零后可以删掉数组分支。
- **CORS `Allow-Origin` 回显请求 origin。** 没有维护白名单，任意来源都能跨域调用。当前 mod 客户端是从 Unity HTTP 直发，本身不受 CORS 约束；这条限制主要影响以后浏览器场景。

## Data Retention

- D1 行**不会**自动删除。`run_bundles`、`runs`、`battles`、`replay_tokens` 持续累加，需要外部 GC。
- R2 对象的清理由 **bucket lifecycle 配置**（不在本仓库）负责。`RUN_BUNDLE_RETENTION_DAYS`（当前 5 天）只是写到 object `customMetadata.retention_days` 上的提示。
- D1 与 R2 的清理彼此独立，所以 ghost-battles 查询有可能命中一个已经被 R2 lifecycle 删掉的 artifact——这种情况下下载会返回 410 `artifact_expired`。

## Local Development

```bash
npm install        # 安装依赖
npm run dev        # 本地 wrangler dev
npm test           # vitest worker 测试套件
npm run check      # tsc --noEmit + 严格未用变量检查
npm run deploy     # wrangler deploy
```

## Layout

| 路径 | 内容 |
| --- | --- |
| `src/index.ts` | 路由表 |
| `src/features/v3/` | HTTP handler |
| `src/crypto/` | 密码哈希、token 生成 |
| `src/http/` | JSON 解析、CORS、错误响应 |
| `src/token/` | bearer token 生成 |
| `src/config/`, `src/env.ts` | 环境变量与配置封装 |
| `src/observability.ts` | `logInfo` / `logWarn` 包装 |
| `migrations/` | D1 schema |
| `test/` | Worker 集成测试 |
| `docs/` | 接口文档 |
