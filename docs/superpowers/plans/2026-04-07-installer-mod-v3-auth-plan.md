# Installer-Mod V3 身份与上传 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 installer 驱动、installation 签名认证、`player_account_id` 主键化的 V3 身份与上传链路，并让新版 mod 直接使用 V3 服务端接口。

**Architecture:** 先在 `ModCFServerV3` 中建立独立的 V3 数据模型、路由和服务端配置，再在 installer 中实现 bootstrap observation 读取、首次激活和 installation key 写入，最后在 mod 中实现共享二进制身份文件、`ModOnlineClient`、V3 observation 上传和 indexed run-bundle upload。V3 不复用旧 `client_id / bind-player` 语义，并固定使用 `https://mod-api-v3.bazaarplusplus.com` 作为新 API 域名。`against me` 的查询时间窗口与 RunBundle artifact 保留期都由服务端配置决定，不从客户端请求传入。

**Tech Stack:** TypeScript, Cloudflare Workers, D1, R2, `tsx --test`, SvelteKit, Tauri Rust commands, C#/.NET test projects

---

## File Map

### Server (`ModCFServerV3/`)

- Modify: `ModCFServerV3/src/index.ts`
  - 挂载 V3 路由，不污染旧路由分支
- Modify: `ModCFServerV3/src/types/api.ts`
  - 新增 V3 activate/login/installations/observations/run-bundles 请求体类型
- Modify: `ModCFServerV3/src/types/db.ts`
  - 新增 `users`、`installations`、`installation_sessions`、`installation_observations`、V3 replay token 行类型
- Create: `ModCFServerV3/src/features/v3/activate.ts`
  - 首次激活原子事务
- Create: `ModCFServerV3/src/features/v3/login.ts`
  - `player_username + password` 登录
- Create: `ModCFServerV3/src/features/v3/createInstallation.ts`
  - 已登录用户创建新 installation
- Create: `ModCFServerV3/src/features/v3/uploadObservation.ts`
  - installation-signed observation 写入
- Create: `ModCFServerV3/src/features/v3/uploadRunBundle.ts`
  - V3 indexed run-bundle 上传入口，接收 `projection + artifact`
- Create: `ModCFServerV3/src/features/v3/queryGhostBattles.ts`
  - V3 ghost battle 查询，查询时间窗口由服务端配置控制
- Create: `ModCFServerV3/src/features/v3/createReplayLink.ts`
  - V3 replay-link
- Create: `ModCFServerV3/src/features/v3/downloadReplay.ts`
  - token 下载 replay
- Create: `ModCFServerV3/src/features/v3/requireInstallerSession.ts`
  - installer session 校验
- Create: `ModCFServerV3/src/features/v3/requireInstallationAuth.ts`
  - installation 签名校验
- Create: `ModCFServerV3/src/persistence/v3/users.ts`
- Create: `ModCFServerV3/src/persistence/v3/installations.ts`
- Create: `ModCFServerV3/src/persistence/v3/installerSessions.ts`
- Create: `ModCFServerV3/src/persistence/v3/observations.ts`
- Create: `ModCFServerV3/src/persistence/v3/runBundles.ts`
- Create: `ModCFServerV3/src/persistence/v3/runs.ts`
- Create: `ModCFServerV3/src/persistence/v3/battles.ts`
- Create: `ModCFServerV3/src/persistence/v3/replayTokens.ts`
- Create: `ModCFServerV3/src/crypto/password.ts`
  - 密码 hash/verify
- Create: `ModCFServerV3/src/crypto/v3Signature.ts`
  - canonical request string、timestamp、body hash 校验
- Create: `ModCFServerV3/src/config/v3.ts`
  - 读取 `ghost_query_lookback_days`、`run_bundle_retention_days`、`battle_ingest_min_rating`、`battle_ingest_min_day_if_below_rating`，并提供默认值
- Modify: `ModCFServerV3/test/helpers/mockEnv.ts`
  - 支持 V3 表、session、installation 行为
- Create: `ModCFServerV3/test/v3.activate.test.ts`
- Create: `ModCFServerV3/test/v3.login.test.ts`
- Create: `ModCFServerV3/test/v3.installations.test.ts`
- Create: `ModCFServerV3/test/v3.observations.test.ts`
- Create: `ModCFServerV3/test/v3.runBundles.test.ts`
- Create: `ModCFServerV3/test/v3.ghostBattles.test.ts`
- Create: `ModCFServerV3/test/v3.replays.test.ts`
- Modify: `ModCFServerV3/test/schema.test.ts`
  - 覆盖 V3 表和索引

### Installer (`../bazaarplusplus-installer/`)

- Create: `../bazaarplusplus-installer/src/lib/identity/codec.ts`
  - `.bpp` envelope 编解码
- Create: `../bazaarplusplus-installer/src/lib/identity/types.ts`
  - observation / installation payload 类型
- Create: `../bazaarplusplus-installer/src/lib/identity/api.ts`
  - 调 V3 activate/login/installations API
- Create: `../bazaarplusplus-installer/src/lib/identity/state.ts`
  - 组合 UI 所需状态机
- Create: `../bazaarplusplus-installer/src/lib/identity/state.test.ts`
- Create: `../bazaarplusplus-installer/src/lib/identity/api.test.ts`
- Modify: `../bazaarplusplus-installer/src/routes/+page.svelte`
  - 增加 observation 检测、首次激活、重新登录、生成 key 的 UI
- Modify: `../bazaarplusplus-installer/src/lib/installer/api.ts`
  - 衔接新的 Tauri commands
- Create: `../bazaarplusplus-installer/src-tauri/src/commands/identity.rs`
  - 读取/写入 `GameRoot/BazaarPlusPlus/Identity/*.bpp`
- Modify: `../bazaarplusplus-installer/src-tauri/src/lib.rs`
  - 注册 identity commands

### Mod (`Game/`, `tests/`)

- Create: `Game/Identity/BppIdentityEnvelope.cs`
  - `.bpp` envelope 读写
- Create: `Game/Identity/PlayerObservationRecord.cs`
- Create: `Game/Identity/InstallationRecord.cs`
- Create: `Game/Identity/IdentityPaths.cs`
- Create: `Game/Identity/PlayerObservationStore.cs`
- Create: `Game/Identity/InstallationRecordStore.cs`
- Create: `Game/Online/ModOnlineClient.cs`
- Create: `Game/Online/InstallationRequestSigner.cs`
- Create: `Game/Online/V2Routes.cs`
- Create: `Game/Online/ObservationClient.cs`
- Create: `Game/Online/RunBundleClient.cs`
- Create: `Game/Online/GhostBattleClient.cs`
- Create: `Game/Online/ReplayClient.cs`
- Create: `Game/Online/Models/RunBundleUploadRequestV2.cs`
- Create: `Game/Online/Models/RunProjectionV2.cs`
- Create: `Game/Online/Models/BattleProjectionV2.cs`
- Create: `Game/Online/Models/RunArtifactV2.cs`
- Create: `Game/Online/Models/RunArtifactBattleV2.cs`
- Create: `Game/Online/Models/BattleManifestArtifactV2.cs`
- Create: `Game/Online/Models/BattleParticipantsArtifactV2.cs`
- Create: `Game/Online/Models/BattleSnapshotsArtifactV2.cs`
- Create: `Game/Online/Models/CardSetCaptureArtifactV2.cs`
- Create: `Game/Online/Models/ReplayPayloadArtifactV2.cs`
- Modify: `Plugin.cs`
  - 挂载新的在线层 bootstrap
- Modify: `Game/HistoryPanel/Ghost/GhostBattleSyncService.cs`
  - 切换到 `ModOnlineClient`
- Modify: `Game/RunLogging/Upload/RunUploadController.cs`
- Modify: `Game/RunLogging/Upload/RunSummaryUploadService.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadController.cs`
- Modify: `Game/CombatReplay/Upload/BattleArtifactUploadService.cs`
  - 旧上传路径退役或改走 V3 run-bundle/upload client
- Create: `tests/GhostBattleSync.Tests/V2IdentityBootstrapTests.cs`
- Create: `tests/RunUploadAuth.Tests/V2InstallationRequestSignerTests.cs`
- Create: `tests/RunUploadBootstrap.Tests/InstallationRecordStoreTests.cs`

## Task 1: 建立 V3 服务端 Schema 与路由骨架

**Files:**
- Create: `ModCFServerV3/`
- Create: `ModCFServerV3/package.json`
- Create: `ModCFServerV3/tsconfig.json`
- Create: `ModCFServerV3/tsconfig.test.json`
- Create: `ModCFServerV3/wrangler.toml`
- Modify: `ModCFServerV3/src/index.ts`
- Modify: `ModCFServerV3/src/types/db.ts`
- Create: `ModCFServerV3/src/persistence/v3/users.ts`
- Create: `ModCFServerV3/src/persistence/v3/installations.ts`
- Create: `ModCFServerV3/src/persistence/v3/installerSessions.ts`
- Create: `ModCFServerV3/src/persistence/v3/installationNonces.ts`
- Create: `ModCFServerV3/src/persistence/v3/observations.ts`
- Create: `ModCFServerV3/src/persistence/v3/runBundles.ts`
- Create: `ModCFServerV3/src/persistence/v3/replayTokens.ts`
- Modify: `ModCFServerV3/test/helpers/mockEnv.ts`
- Test: `ModCFServerV3/test/schema.test.ts`
- Test: `ModCFServerV3/test/index.test.ts`

- [ ] **Step 1: 创建 `ModCFServerV3/` 最小脚手架**

从现有 `ModCFServer/` 复制最小运行骨架到新目录，但不要把旧 feature 代码直接当成 V3 实现沿用。

至少准备：

- `package.json`
- `tsconfig.json`
- `tsconfig.test.json`
- `wrangler.toml`
- `src/index.ts`
- `src/http/`
- `src/crypto/`
- `src/env.ts`
- `test/helpers/mockEnv.ts`

命令建议：

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
mkdir -p ModCFServerV3
rsync -a ModCFServer/ ModCFServerV3/ \
  --exclude node_modules \
  --exclude .wrangler \
  --exclude dist
```

然后立即删除或清空旧 feature 入口，避免后续误用：

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
rm -rf src/features
mkdir -p src/features/v3 src/persistence/v3 test
```

- [ ] **Step 2: 先写 V3 schema 测试，固定新表与索引**

在 `ModCFServerV3/test/schema.test.ts` 新增断言，至少覆盖：

```ts
assert.match(sql, /\bCREATE TABLE users\b/);
assert.match(sql, /\bCREATE TABLE installations\b/);
assert.match(sql, /\bCREATE TABLE installation_sessions\b/);
assert.match(sql, /\bCREATE TABLE installation_observations\b/);
assert.match(sql, /\bplayer_account_id TEXT PRIMARY KEY\b/);
assert.match(sql, /\bplayer_username TEXT NOT NULL UNIQUE\b/);
```

- [ ] **Step 3: 跑 schema 测试，确认当前失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/schema.test.ts test/index.test.ts
```

Expected:

- FAIL，提示 V3 表或路由尚不存在

- [ ] **Step 4: 加入 V3 表定义与 persistence 骨架**

在 `ModCFServerV3/src/types/db.ts` 增加最小行类型：

```ts
export type V2UserRow = {
  player_account_id: string;
  player_username: string;
  password_hash: string;
  stream_platform: string | null;
  stream_channel_id: string | null;
  stream_url: string | null;
  created_at_utc: string;
  updated_at_utc: string;
  last_login_at_utc: string | null;
};
```

在 `ModCFServerV3/src/index.ts` 先挂空 handler 路由：

```ts
if (request.method === "POST" && url.pathname === "/activate") {
  return json({ error: "not_implemented" }, { status: 501 });
}
```

- [ ] **Step 5: 更新 mock env，支持 V3 表读取和最小路由 smoke**

在 `ModCFServerV3/test/helpers/mockEnv.ts` 增加 V3 表内存结构：

```ts
type MockV2UserRow = {
  player_account_id: string;
  player_username: string;
  password_hash: string;
};

const users = new Map<string, MockV2UserRow>();
const installations = new Map<string, MockInstallationRow>();
```

- [ ] **Step 6: 重新跑 schema/index 测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/schema.test.ts test/index.test.ts
```

Expected:

- PASS，V3 表和占位路由存在

## Task 2: 落地 installer session 与首次激活/登录接口

**Files:**
- Create: `ModCFServerV3/src/crypto/password.ts`
- Create: `ModCFServerV3/src/features/v3/activate.ts`
- Create: `ModCFServerV3/src/features/v3/login.ts`
- Create: `ModCFServerV3/src/features/v3/requireInstallerSession.ts`
- Modify: `ModCFServerV3/src/index.ts`
- Modify: `ModCFServerV3/src/types/api.ts`
- Modify: `ModCFServerV3/test/helpers/mockEnv.ts`
- Test: `ModCFServerV3/test/v3.activate.test.ts`
- Test: `ModCFServerV3/test/v3.login.test.ts`

- [ ] **Step 1: 写首次激活测试，固定“原子占用 player_account_id”**

在 `ModCFServerV3/test/v3.activate.test.ts` 先写：

```ts
test("activate creates user and first installation atomically", async () => {
  const response = await app.request("/activate", { method: "POST", body: payload });
  assert.equal(response.status, 200);
  assert.equal(env.v3.users.size, 1);
  assert.equal(env.v3.installations.size, 1);
});

test("activate rejects already-claimed player_account_id", async () => {
  assert.equal(response.status, 409);
});
```

- [ ] **Step 2: 写登录测试，固定 `player_username + password` 登录**

在 `ModCFServerV3/test/v3.login.test.ts` 先写：

```ts
test("login returns installer session for matching password", async () => {
  assert.equal(response.status, 200);
  assert.match(json.session_token, /^sess_/);
});

test("login rejects invalid password", async () => {
  assert.equal(response.status, 401);
});
```

- [ ] **Step 3: 跑 activate/login 测试确认失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/v3.activate.test.ts test/v3.login.test.ts
```

Expected:

- FAIL，提示 handler 或 password helper 未实现

- [ ] **Step 4: 实现密码 hash、installer session 和 activate/login handler**

`ModCFServerV3/src/crypto/password.ts` 最小 API：

```ts
export async function hashPassword(password: string): Promise<string> {}
export async function verifyPassword(password: string, hash: string): Promise<boolean> {}
```

`ModCFServerV3/src/features/v3/activate.ts` 最小骨架：

```ts
export async function handleActivate(request: Request, env: Env): Promise<Response> {
  // validate payload
  // begin atomic create user + installation
}
```

- [ ] **Step 5: 重新跑 activate/login 测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/v3.activate.test.ts test/v3.login.test.ts
```

Expected:

- PASS

- [ ] **Step 6: 跑 TypeScript 检查**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npm run check
```

Expected:

- PASS

## Task 3: 落地 installation 签名认证和 V3 observation/replay 授权

**Files:**
- Create: `ModCFServerV3/src/crypto/v3Signature.ts`
- Create: `ModCFServerV3/src/config/v3.ts`
- Create: `ModCFServerV3/src/features/v3/requireInstallationAuth.ts`
- Create: `ModCFServerV3/src/features/v3/uploadObservation.ts`
- Create: `ModCFServerV3/src/features/v3/queryGhostBattles.ts`
- Create: `ModCFServerV3/src/features/v3/createReplayLink.ts`
- Create: `ModCFServerV3/src/features/v3/downloadReplay.ts`
- Modify: `ModCFServerV3/src/index.ts`
- Modify: `ModCFServerV3/test/helpers/mockEnv.ts`
- Test: `ModCFServerV3/test/v3.observations.test.ts`
- Test: `ModCFServerV3/test/v3.ghostBattles.test.ts`
- Test: `ModCFServerV3/test/v3.replays.test.ts`

- [ ] **Step 1: 写 installation auth 测试，固定 canonical request 语义**

在 `ModCFServerV3/test/v3.observations.test.ts` 先写：

```ts
test("installation-signed observation is accepted once", async () => {
  assert.equal(response.status, 200);
});
```

- [ ] **Step 2: 写 replay 授权测试，固定两段式 replay-link/download**

在 `ModCFServerV3/test/v3.replays.test.ts` 先写：

```ts
test("replay-link requires battle ownership", async () => {
  assert.equal(response.status, 403);
});

test("download replay accepts valid short-lived token", async () => {
  assert.equal(response.status, 200);
});

test("download replay returns artifact_expired when artifact is no longer available", async () => {
  assert.equal(response.status, 410);
});
```

- [ ] **Step 3: 跑 observation/ghost/replay 测试确认失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/v3.observations.test.ts test/v3.ghostBattles.test.ts test/v3.replays.test.ts
```

Expected:

- FAIL，提示签名验证与 replay-link 路由未实现

- [ ] **Step 4: 实现 canonical request 和 installation auth**

`ModCFServerV3/src/crypto/v3Signature.ts` 最小 API：

```ts
export function buildCanonicalRequest(input: {
  method: string;
  path: string;
  query: string;
  installationId: string;
  timestamp: string;
  bodyHash: string;
}): string {}
```

- [ ] **Step 5: 实现 observation、ghost query、replay-link、token download**

`createReplayLink.ts` 核心检查：

```ts
if (battle.player_account_id !== auth.playerAccountId) {
  return json({ error: "battle_not_owned" }, { status: 403 });
}
```

`queryGhostBattles.ts` 还应明确：

- 不接收客户端传入的 lookback days
- 从 `src/config/v3.ts` 读取 `ghost_query_lookback_days`
- 默认值固定为 3 天
- 服务端按该配置计算 `recorded_at_utc` 下界

`downloadReplay.ts` 还应明确：

- 如果 replay token 仍有效，但底层 artifact 已过期或对象缺失
- 返回 `410 Gone`
- 错误码固定为 `artifact_expired`

- [ ] **Step 6: 重新跑 V3 auth/query/replay 测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/v3.observations.test.ts test/v3.ghostBattles.test.ts test/v3.replays.test.ts
```

Expected:

- PASS

## Task 4: 落地 V3 run-bundle 上传入口

**Files:**
- Create: `ModCFServerV3/src/features/v3/uploadRunBundle.ts`
- Create: `ModCFServerV3/test/v3.runBundles.test.ts`
- Modify: `ModCFServerV3/src/index.ts`
- Modify: `ModCFServerV3/src/persistence/v3/runBundles.ts`
- Create: `ModCFServerV3/src/persistence/v3/runs.ts`
- Create: `ModCFServerV3/src/persistence/v3/battles.ts`

- [ ] **Step 1: 写 run-bundle 上传测试，固定 `projection + artifact` 语义**

在 `ModCFServerV3/test/v3.runBundles.test.ts` 先写：

```ts
test("run bundle upload stores one artifact object and projection rows", async () => {
  assert.equal(response.status, 200);
  assert.equal(env.bucket.objects.size, 1);
  assert.equal(env.v3.runBundles.size, 1);
  assert.equal(env.v3.runs.size, 1);
  assert.equal(env.v3.battles.size, 2);
});

test("run bundle upload rejects duplicated battle ids in battle_projections", async () => {
  assert.equal(response.status, 400);
});

test("run bundle upload rejects mismatched battle run ids", async () => {
  assert.equal(response.status, 400);
});

test("run bundle upload applies configured artifact retention", async () => {
  assert.equal(response.status, 200);
  assert.equal(env.bucket.lastPutOptions?.customMetadata?.retention_days, "5");
});

test("run bundle upload only projects battles that pass ingest gate", async () => {
  assert.equal(response.status, 200);
  assert.equal(env.v3.battles.size, 1);
});
```

- [ ] **Step 2: 跑 run-bundle 测试确认失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/v3.runBundles.test.ts
```

Expected:

- FAIL，提示 `/run-bundles` 未实现

- [ ] **Step 3: 实现最小 indexed run-bundle 接口**

在 `uploadRunBundle.ts` 先只做：

```ts
// requireInstallationAuth
// validate top-level schema_version
// validate run_projection.run_id presence
// validate battle_projections[].battle_id presence and uniqueness
// validate every battle_projection.run_id == run_projection.run_id
// artifact contains complete manifest + replay per battle
// store artifact_bytes in R2
// upsert run_bundles metadata row
// upsert runs projection row
// replace battles rows for the run using only gate-approved battle_projections
```

并补充：

- 从 `src/config/v3.ts` 读取 `run_bundle_retention_days`
- 默认值固定为 5 天
- 写 R2 对象时同时写入该保留期对应的过期时间或等价元数据
- 从 `src/config/v3.ts` 读取 `battle_ingest_min_rating`
- 从 `src/config/v3.ts` 读取 `battle_ingest_min_day_if_below_rating`
- 当 battle 的 rating 大于等于门槛时直接入表
- 当 battle 的 rating 低于门槛时，只有 `battle.day` 大于等于补充门槛才入表

- [ ] **Step 4: 跑 run-bundle 测试与整体 V3 测试**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/v3.*.test.ts
npm run check
```

Expected:

- PASS

## Task 5: 在 installer 中实现共享 identity 文件和首次激活/重新登录状态机

**Files:**
- Create: `../bazaarplusplus-installer/src/lib/identity/codec.ts`
- Create: `../bazaarplusplus-installer/src/lib/identity/types.ts`
- Create: `../bazaarplusplus-installer/src/lib/identity/api.ts`
- Create: `../bazaarplusplus-installer/src/lib/identity/state.ts`
- Create: `../bazaarplusplus-installer/src/lib/identity/state.test.ts`
- Create: `../bazaarplusplus-installer/src/lib/identity/api.test.ts`
- Create: `../bazaarplusplus-installer/src-tauri/src/commands/identity.rs`
- Modify: `../bazaarplusplus-installer/src-tauri/src/lib.rs`
- Modify: `../bazaarplusplus-installer/src/lib/installer/api.ts`
- Modify: `../bazaarplusplus-installer/src/routes/+page.svelte`

- [ ] **Step 1: 写 identity state 测试，固定四种 UI 状态**

在 `../bazaarplusplus-installer/src/lib/identity/state.test.ts` 先写：

```ts
test("no observation blocks first activation", () => {});
test("observation enables first activation", () => {});
test("logged-in user with changed observation requires re-login", () => {});
test("logged-in user with no new observation can still view profile", () => {});
```

- [ ] **Step 2: 写 codec 测试，固定 `.bpp` envelope 读写**

在 `../bazaarplusplus-installer/src/lib/identity/api.test.ts` 或新 test 中写：

```ts
test("decode rejects wrong magic", () => {});
test("decode rejects checksum mismatch", () => {});
```

- [ ] **Step 3: 跑 installer focused tests 确认失败**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-installer
node --test src/lib/identity/*.test.ts
```

Expected:

- FAIL，identity 模块尚不存在

- [ ] **Step 4: 实现 Tauri identity commands 和前端 codec/state**

`identity.rs` 至少提供：

```rust
#[tauri::command]
fn read_player_observation(game_root: String) -> Result<Option<PlayerObservationPayload>, String> {}

#[tauri::command]
fn write_installation_record(game_root: String, payload: InstallationPayload) -> Result<(), String> {}
```

- [ ] **Step 5: 在 `+page.svelte` 接入首次激活与重新登录 UI**

增加最小状态分支：

```ts
if (identityState.kind === "observation_required") { ... }
if (identityState.kind === "activate_first_account") { ... }
if (identityState.kind === "relogin_required") { ... }
```

- [ ] **Step 6: 跑 installer 测试和类型检查**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-installer
node --test src/lib/identity/*.test.ts
npm run check
```

Expected:

- PASS

## Task 6: 在 mod 中实现共享 identity 文件、installation 签名和 `ModOnlineClient`

**Files:**
- Create: `Game/Identity/BppIdentityEnvelope.cs`
- Create: `Game/Identity/PlayerObservationRecord.cs`
- Create: `Game/Identity/InstallationRecord.cs`
- Create: `Game/Identity/IdentityPaths.cs`
- Create: `Game/Identity/PlayerObservationStore.cs`
- Create: `Game/Identity/InstallationRecordStore.cs`
- Create: `Game/Online/ModOnlineClient.cs`
- Create: `Game/Online/InstallationRequestSigner.cs`
- Create: `Game/Online/V2Routes.cs`
- Create: `Game/Online/ObservationClient.cs`
- Create: `Game/Online/RunBundleClient.cs`
- Create: `Game/Online/Models/RunBundleUploadRequestV2.cs`
- Create: `Game/Online/Models/RunProjectionV2.cs`
- Create: `Game/Online/Models/BattleProjectionV2.cs`
- Create: `Game/Online/Models/RunArtifactV2.cs`
- Create: `Game/Online/Models/RunArtifactBattleV2.cs`
- Create: `Game/Online/Models/BattleManifestArtifactV2.cs`
- Create: `Game/Online/Models/BattleParticipantsArtifactV2.cs`
- Create: `Game/Online/Models/BattleSnapshotsArtifactV2.cs`
- Create: `Game/Online/Models/CardSetCaptureArtifactV2.cs`
- Create: `Game/Online/Models/ReplayPayloadArtifactV2.cs`
- Test: `tests/RunUploadAuth.Tests/V2InstallationRequestSignerTests.cs`
- Test: `tests/RunUploadBootstrap.Tests/InstallationRecordStoreTests.cs`

- [ ] **Step 1: 写 envelope/store 测试**

在 `tests/RunUploadBootstrap.Tests/Program.cs` 附近新增：

```csharp
Fact("installation record rejects wrong magic", () => { });
Fact("installation record rejects checksum mismatch", () => { });
```

- [ ] **Step 2: 写 request signer 测试**

在 `tests/RunUploadAuth.Tests/Program.cs` 附近新增：

```csharp
Fact("installation signer includes timestamp and content hash in canonical request", () => { });
```

- [ ] **Step 3: 跑 focused C# tests 确认失败**

Run:

```bash
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadBootstrap.Tests/RunUploadBootstrap.Tests.csproj
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadAuth.Tests/RunUploadAuth.Tests.csproj
```

Expected:

- FAIL，新的 identity/online 类型尚不存在

- [ ] **Step 4: 实现 `.bpp` envelope、identity stores、routes、request signer**

`InstallationRequestSigner.cs` 最小接口：

```csharp
internal sealed class InstallationRequestSigner
{
    public HttpRequestMessage CreateSignedRequest(
        HttpMethod method,
        string endpoint,
        byte[] bodyBytes,
        InstallationRecord installation,
        string timestamp)
    { ... }
}
```

- [ ] **Step 5: 实现 `ModOnlineClient`、`ObservationClient` 与 `RunBundleClient` 骨架**

```csharp
internal sealed class ModOnlineClient
{
    public Task PublishObservationAsync(CancellationToken cancellationToken) { ... }
    public Task UploadRunBundleAsync(CancellationToken cancellationToken) { ... }
}
```

新增模型至少包括：

```csharp
internal sealed class RunBundleUploadRequestV2
{
    public int SchemaVersion { get; init; }
    public string InstallationId { get; init; } = string.Empty;
    public string PlayerAccountId { get; init; } = string.Empty;
    public RunProjectionV3 RunProjection { get; init; } = new();
    public IReadOnlyList<BattleProjectionV2> BattleProjections { get; init; } = Array.Empty<BattleProjectionV2>();
    public string ArtifactCodec { get; init; } = string.Empty;
    public byte[] ArtifactBytes { get; init; } = Array.Empty<byte>();
}
```

`RunProjectionV2` 还应至少包含：

- `Rating`
- `Rank`

不要只采 run 级 `rating`，遗漏 run 级 rank 信息。

其中 `RunArtifactBattleV2` 应至少包含：

```csharp
internal sealed class RunArtifactBattleV2
{
    public string BattleId { get; init; } = string.Empty;
    public BattleManifestArtifactV3 Manifest { get; init; } = new();
    public ReplayPayloadArtifactV3 Replay { get; init; } = new();
}
```

`BattleManifestArtifactV2` 应包含完整 manifest 信息，包括：

- participants
- result
- snapshots

其中 participants 的采集语义应固定为：

- `player_rank` / `opponent_rank` 只采 rank 名字
- `player_level` / `opponent_level` 继续采玩家等级
- 不要把 rank 的内部级别拼进 `player_rank` / `opponent_rank`

不要把 `snapshots` 再拆成独立顶层 artifact；它仍属于 manifest 的一部分。

- [ ] **Step 6: 重新跑 focused C# tests**

Run:

```bash
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadBootstrap.Tests/RunUploadBootstrap.Tests.csproj
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadAuth.Tests/RunUploadAuth.Tests.csproj
```

Expected:

- PASS

## Task 7: 用 `ModOnlineClient` 接管 ghost/query 与上传路径

**Files:**
- Modify: `Game/HistoryPanel/Ghost/GhostBattleSyncService.cs`
- Modify: `Game/RunLogging/Upload/RunUploadController.cs`
- Modify: `Game/RunLogging/Upload/RunSummaryUploadService.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadController.cs`
- Modify: `Game/CombatReplay/Upload/BattleArtifactUploadService.cs`
- Modify: `Plugin.cs`
- Test: `tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj`
- Test: `tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj`

- [ ] **Step 1: 写 ghost sync 切换测试**

在 `tests/GhostBattleSync.Tests/Program.cs` 附近新增：

```csharp
Fact("ghost sync reads installation record and uses V3 replay-link flow", () => { });
```

- [ ] **Step 2: 写 upload sync 切换测试**

在 `tests/RunUploadSync.Tests/Program.cs` 附近新增：

```csharp
Fact("run upload uses ModOnlineClient V3 route when installation is active", () => { });
```

- [ ] **Step 3: 跑 focused mod integration tests 确认失败**

Run:

```bash
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj
```

Expected:

- FAIL，旧路径仍在使用旧 auth/upload logic

- [ ] **Step 4: 在 `Plugin.cs` 注入新的在线层 bootstrap**

最小接线目标：

```csharp
var onlineClient = new ModOnlineClient(...);
```

- [ ] **Step 5: 将 ghost/replay 流程切换到 replay-link + token 下载**

`GhostBattleSyncService.cs` 应从：

```csharp
CreateAuthenticatedRouteClient()
```

切换为依赖新的 `ModOnlineClient`。

- [ ] **Step 6: 将 run/battle 上传控制器切换到 V3 入口**

要求：

- 优先走 `/run-bundles`
- 不再依赖旧 `client_id` 与 `bind-player`
- 按 spec 组装 `RunBundleUploadRequestV2`，其中查询字段放进 `run_projection` / `battle_projections`，完整回放内容只进入 `artifact`
- 当 installation 未激活时保持安全降级，不伪造身份

- [ ] **Step 7: 跑 focused tests 和最小相关检查**

Run:

```bash
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadAuth.Tests/RunUploadAuth.Tests.csproj
```

Expected:

- PASS

## Task 8: 收尾验证

**Files:**
- Modify: `docs/superpowers/specs/2026-04-07-installer-mod-v3-auth-design.md`
  - 如实现偏差需要回写

- [ ] **Step 1: 跑服务端完整 V3 测试集**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/v3.*.test.ts
npm run check
```

Expected:

- PASS

- [ ] **Step 2: 跑 installer focused checks**

Run:

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-installer
node --test src/lib/identity/*.test.ts
npm run check
```

Expected:

- PASS

- [ ] **Step 3: 跑 mod focused checks**

Run:

```bash
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadBootstrap.Tests/RunUploadBootstrap.Tests.csproj
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadAuth.Tests/RunUploadAuth.Tests.csproj
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj
dotnet test /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj
```

Expected:

- PASS

- [ ] **Step 4: 手工冒烟一次首次激活**

Manual flow:

1. 删除现有 `GameRoot/BazaarPlusPlus/Identity/*`
2. 启动游戏与 mod，确认生成 `player-observation.bpp`
3. 打开 installer，走首次激活
4. 确认生成 `installation.bpp` 和 `installation.key`
5. 触发一次 observation 上传和一次 V3 run-bundle 上传

Expected:

- installer 正常显示 observation
- 首次激活成功
- mod 能在 installer 不运行时继续发起 V3 请求

## Self-Review

- Spec coverage:
  - account model / activation flow -> Task 1, 2, 5
  - shared `.bpp` files -> Task 5, 6
  - installation signing -> Task 3, 6
  - V3 run-bundle path -> Task 4, 7
  - replay-link + token download -> Task 3, 7
  - final verification -> Task 8
- Placeholder scan:
  - 无 `TODO` / `TBD`
  - 每个阶段都有具体文件和验证命令
- Type consistency:
  - 统一使用 `player_account_id`、`player_username`、`installation_id`
  - 不再在计划中复用旧 `client_id` / `install_id` 名称
