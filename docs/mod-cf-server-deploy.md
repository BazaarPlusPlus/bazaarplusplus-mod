# ModCFServer Deployment

本文描述当前仓库里的 `ModCFServer` 部署形态，基于 `ModCFServer/wrangler.toml`、`ModCFServer/package.json` 和 `ModCFServer/src/index.ts`。

## Current Targets

- worker name: `bazaarplusplus-mod-api`
- custom domain: `mod-api.bazaarplusplus.com`
- D1 database: `bazaarplusplus-mod-api-db`
- D1 migrations dir: `ModCFServer/migrations`
- R2 bucket: `bazaarplusplus-replays`
- cron trigger: every 6 hours

客户端默认使用：

- `https://mod-api.bazaarplusplus.com/clients/register`
- `https://mod-api.bazaarplusplus.com/runs/upload`

## Setup

从仓库根目录：

```powershell
cd ModCFServer
npm install
npx wrangler login
```

创建 D1：

```powershell
npx wrangler d1 create bazaarplusplus-mod-api-db
```

把返回的 `database_id` 写回 `ModCFServer/wrangler.toml`。

创建 R2：

```powershell
npx wrangler r2 bucket create bazaarplusplus-replays
```

创建回放下载签名密钥：

```powershell
npx wrangler secret put REPLAY_DOWNLOAD_SECRET
```

## Deploy

```powershell
npm run deploy
```

`package.json` 当前映射为 `wrangler deploy`。

## Verify

健康检查当前是：

```powershell
curl https://mod-api.bazaarplusplus.com/health
```

预期响应：

```json
{"ok":true}
```

当前路由：

- `GET /health`
- `POST /clients/register`
- `POST /runs/upload`
- `POST /replays/upload`
- `GET /me/pvp-battles/against-me`
- `POST /me/pvp-battles/:battleId/replay-download-link`
- `GET /replays/download`

## Notes

- 当前入口 `ModCFServer/src/index.ts` 不负责按请求懒建表。
- D1 schema 由 Wrangler migration 管理，见 `ModCFServer/migrations/0001_initial_schema.sql`。
- 定时任务会调用 `purgeExpiredNonces(...)` 清理过期 nonce。
