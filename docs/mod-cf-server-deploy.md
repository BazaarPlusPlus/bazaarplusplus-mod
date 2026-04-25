# ModCFServerV3 Deployment

本文描述 `ModCFServerV3` 的部署要点，基于 `ModCFServerV3/wrangler.toml`、`ModCFServerV3/migrations/`、`ModCFServerV3/package.json` 和当前 Worker 路由实现。

## Targets

- Worker config: `ModCFServerV3/wrangler.toml`
- D1 migrations dir: `ModCFServerV3/migrations`
- Runtime entry: `ModCFServerV3/src/index.ts`

当前 Worker 路由包括：

- `GET /health`
- `POST /activate`
- `POST /login`
- `POST /logout`
- `POST /run-bundles`
- `GET /ghost-battles`
- `POST /ghost-battles/:battleId/replay-link`
- `GET /replays/:token`

## First Deploy

从仓库根目录：

```powershell
cd ModCFServerV3
npm install
npx wrangler login
```

1. 创建 D1：

```powershell
npx wrangler d1 create bazaarplusplus-mod-api-v3-db
```

把返回的 `database_id` 写回 `ModCFServerV3/wrangler.toml` 的 `[[d1_databases]]` 配置。如果生产配置已经有真实 id，不要替换成占位值。

2. 应用 migrations：

```powershell
npx wrangler d1 migrations apply bazaarplusplus-mod-api-v3-db
```

3. 创建 R2 bucket：

```powershell
npx wrangler r2 bucket create bazaarplusplus-run-bundles-v3
```

4. 部署 Worker：

```powershell
npm run deploy
```

## Verify

健康检查：

```powershell
curl https://mod-api-v3.bazaarplusplus.com/health
```

预期响应：

```json
{"ok":true}
```

建议 smoke test 至少覆盖：

- `POST /activate`
- `POST /login`
- `POST /logout`
- `POST /run-bundles`
- `GET /ghost-battles`
- `POST /ghost-battles/:battleId/replay-link`
- `GET /replays/:token`

## Notes

- D1 schema 由 Wrangler migration 管理，见 `ModCFServerV3/migrations/`。
- replay 对象与 token 行为由 `createReplayLink` / `downloadReplay` 路由负责。
- 当前 V3 预期资源名为 `bazaarplusplus-mod-api-v3`、`mod-api-v3.bazaarplusplus.com`、`bazaarplusplus-mod-api-v3-db`、`bazaarplusplus-run-bundles-v3`。
- `ALLOW_UNAUTHENTICATED_REPLAY_LINKS` 和 `ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS` 是早期 rollout 兼容开关；收紧鉴权时应成对调整。
- 如果生产环境已存在实际资源名，以 `ModCFServerV3/wrangler.toml` 为准。
