# ModCFServer Deployment

本文描述 `ModCFServer` 的首次正式部署流程，基于 `ModCFServer/wrangler.toml`、`ModCFServer/migrations/0001_initial_schema.sql`、`ModCFServer/package.json` 和当前 Worker 路由实现。

## Targets

- Worker name: `bazaarplusplus-mod-api`
- Custom domain: `mod-api.bazaarplusplus.com`
- D1 database: `bazaarplusplus-mod-api-db`
- D1 migrations dir: `ModCFServer/migrations`
- R2 bucket: `bazaarplusplus-replays`
- Cron trigger: every 6 hours

当前首发方案使用单个 R2 bucket，同时承载：

- `runs/{client_id}/{run_id}/{payload_sha256}.json`
- `replays/{client_id}/{battle_id}/{payload_sha256}.json`

## First Deploy

从仓库根目录：

```powershell
cd ModCFServer
npm install
npx wrangler login
```

1. 创建 D1：

```powershell
npx wrangler d1 create bazaarplusplus-mod-api-db
```

把返回的 `database_id` 写回 `ModCFServer/wrangler.toml` 的 `[[d1_databases]]` 配置。

2. 应用初始 schema：

```powershell
npx wrangler d1 migrations apply bazaarplusplus-mod-api-db
```

3. 创建 R2 bucket：

```powershell
npx wrangler r2 bucket create bazaarplusplus-replays
```

4. 写入回放下载签名密钥：

```powershell
npx wrangler secret put REPLAY_DOWNLOAD_SECRET
```

5. 部署 Worker：

```powershell
npm run deploy
```

## Verify

健康检查：

```powershell
curl https://mod-api.bazaarplusplus.com/health
```

预期响应：

```json
{"ok":true}
```

首发 smoke test 至少覆盖：

- `POST /clients/register`
- `POST /clients/bind`
- `POST /runs/upload`
- `POST /replays/upload`
- `GET /me/pvp-battles/against-me`

建议使用已注册并验签的测试客户端，确认：

- `client_player_account_bindings` 写入 active binding
- run payload 落到 `runs/...`
- replay payload 落到 `replays/...`
- ghost battle 查询能返回 `replay.available`

## Notes

- 当前入口 `ModCFServer/src/index.ts` 不负责按请求懒建表。
- D1 schema 由 Wrangler migration 管理，见 `ModCFServer/migrations/0001_initial_schema.sql`。
- 定时任务会调用 `purgeExpiredNonces(...)` 清理过期 nonce。
