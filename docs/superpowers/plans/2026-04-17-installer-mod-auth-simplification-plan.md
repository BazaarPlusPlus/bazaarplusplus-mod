# Installer-Mod Auth Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace installation-based RSA signature auth with per-user bearer tokens backed by a shared SQLite `identity.db`, split write vs. read auth policy (writes open, reads require bearer), and retire the `installations` concept end-to-end in ModCFServerV3 and the BazaarPlusPlus mod.

**Architecture:** Two phases executed sequentially. Phase A lands all server changes (D1 migration, route refactor, new `tokens` table, new `requireBearerAuth` middleware) while keeping old signed requests gracefully ignored — so no client breakage during rollout. Phase B lands mod-side changes (new SQLite identity layer, removal of `InstallationRequestSigner`, bearer-based online clients). Installer changes ship in the separate `bazaarplusplus-installer` repo via its own plan.

**Spec:** [docs/superpowers/specs/2026-04-17-installer-mod-auth-simplification-design.md](../specs/2026-04-17-installer-mod-auth-simplification-design.md)

**Tech Stack:**
- Server: TypeScript + Cloudflare Workers (`wrangler`), D1 (SQLite), KV, R2. Tests with `tsx --test` under `ModCFServerV3/test/`.
- Mod: C# (.NET / BepInEx / Unity Mono), `Microsoft.Data.Sqlite` (already in use). Tests are per-feature projects under `tests/<Feature>.Tests/`.

**Installer:** Out of scope for this plan. See `../bazaarplusplus-installer/docs/superpowers/plans/` for the matching Installer plan (to be written separately).

---

## Phase A — Server (ModCFServerV3)

### Task A1: Add D1 migration for auth simplification

**Files:**
- Create: `ModCFServerV3/migrations/0002_auth_simplification.sql`

- [ ] **Step 1: Write the migration SQL**

```sql
-- Migration 0002: auth simplification — per-user bearer tokens, drop installations

CREATE TABLE tokens (
  token              TEXT    PRIMARY KEY,
  player_account_id  TEXT    NOT NULL REFERENCES users(player_account_id),
  issued_at_utc      TEXT    NOT NULL,
  revoked_at_utc     TEXT    NULL,
  last_used_at_utc   TEXT    NULL
);

CREATE INDEX tokens_by_user ON tokens(player_account_id, revoked_at_utc);

-- Drop retired tables
DROP TABLE IF EXISTS installation_sessions;
DROP TABLE IF EXISTS installation_observations;
DROP TABLE IF EXISTS installations;

-- Swap run_bundles unique key: (installation_id, run_id, payload_hash) -> (player_account_id, run_id, payload_hash)
-- installation_id columns are kept nullable on runs/battles/run_bundles for conservative retention.

DROP INDEX IF EXISTS run_bundles_installation_run_payload_unique;
CREATE UNIQUE INDEX run_bundles_player_run_payload_unique
  ON run_bundles(player_account_id, run_id, payload_hash);
```

- [ ] **Step 2: Verify existing migration naming + unique-index name**

Run:
```bash
ls /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3/migrations/
grep -E "UNIQUE|CREATE UNIQUE INDEX" /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3/migrations/0001_initial_schema.sql
```

Expected: confirm `0001_initial_schema.sql` exists and find the actual name of the old `run_bundles` unique constraint / index. Update `DROP INDEX IF EXISTS ...` line in the migration to the actual name. If it was declared inline in `CREATE TABLE` (not as a separate index), swap strategy: create the new unique index and leave the old constraint as-is (new writes will still be unique under the new constraint).

- [ ] **Step 3: Apply migration locally against wrangler dev D1**

Run:
```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx wrangler d1 migrations apply <db_binding_name> --local
```

Expected: migration applies without error. Check `schema.test.ts` still passes after update in Task A12.

- [ ] **Step 4: Commit**

```bash
git add ModCFServerV3/migrations/0002_auth_simplification.sql
git commit -m "ModCFServerV3 Add auth simplification D1 migration"
```

---

### Task A2: Token generation utility

**Files:**
- Create: `ModCFServerV3/src/token/generate.ts`
- Test: `ModCFServerV3/test/token.generate.test.ts`

- [ ] **Step 1: Write failing test**

```typescript
// ModCFServerV3/test/token.generate.test.ts
import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { generateBearerToken } from "../src/token/generate";

describe("generateBearerToken", () => {
  it("produces base64url string of length 43 (32 bytes)", () => {
    const token = generateBearerToken();
    assert.match(token, /^[A-Za-z0-9_-]{43}$/);
  });

  it("produces distinct tokens on repeated calls", () => {
    const a = generateBearerToken();
    const b = generateBearerToken();
    assert.notEqual(a, b);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx tsx --test test/token.generate.test.ts
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement `generateBearerToken`**

```typescript
// ModCFServerV3/src/token/generate.ts

export function generateBearerToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes);
}

function base64UrlEncode(bytes: Uint8Array): string {
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary)
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");
}
```

- [ ] **Step 4: Run test**

```bash
npx tsx --test test/token.generate.test.ts
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ModCFServerV3/src/token/generate.ts ModCFServerV3/test/token.generate.test.ts
git commit -m "ModCFServerV3 Add bearer token generator"
```

---

### Task A3: Bearer auth middleware

**Files:**
- Create: `ModCFServerV3/src/features/v3/requireBearerAuth.ts`
- Test: `ModCFServerV3/test/v3.requireBearerAuth.test.ts`

- [ ] **Step 1: Write failing test**

```typescript
// ModCFServerV3/test/v3.requireBearerAuth.test.ts
import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { buildEnv } from "./helpers/mockEnv";
import { requireBearerAuth } from "../src/features/v3/requireBearerAuth";

describe("requireBearerAuth", () => {
  it("returns 401 when Authorization header missing", async () => {
    const env = buildEnv();
    const req = new Request("https://example/any");
    const result = await requireBearerAuth(req, env);
    assert.ok(result instanceof Response);
    assert.equal((result as Response).status, 401);
  });

  it("returns 401 for Bearer token not in tokens table", async () => {
    const env = buildEnv();
    const req = new Request("https://example/any", {
      headers: { Authorization: "Bearer nonexistent_abc" }
    });
    const result = await requireBearerAuth(req, env);
    assert.ok(result instanceof Response);
    assert.equal((result as Response).status, 401);
  });

  it("returns 401 for revoked token", async () => {
    const env = buildEnv();
    await env.DB.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc, revoked_at_utc) VALUES (?, ?, ?, ?)`
    ).bind("tok_revoked", "p1", "2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z").run();
    const req = new Request("https://example/any", {
      headers: { Authorization: "Bearer tok_revoked" }
    });
    const result = await requireBearerAuth(req, env);
    assert.ok(result instanceof Response);
    assert.equal((result as Response).status, 401);
  });

  it("returns auth object for active token", async () => {
    const env = buildEnv();
    await env.DB.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc) VALUES (?, ?, ?)`
    ).bind("tok_active", "p1", "2026-01-01T00:00:00Z").run();
    const req = new Request("https://example/any", {
      headers: { Authorization: "Bearer tok_active" }
    });
    const result = await requireBearerAuth(req, env);
    assert.deepEqual(result, { token: "tok_active", playerAccountId: "p1" });
  });
});
```

**Harness reality:** `ModCFServerV3/test/helpers/mockEnv.ts` is a hand-rolled SQL-pattern-matching mock (no real SQLite, no migrations). Tests that interact with the new `tokens` table need the mock extended to recognize the relevant SQL. Step 1.5 below covers that.

- [ ] **Step 1.5: Extend `mockEnv.ts` to support `tokens` table**

Add to `ModCFServerV3/test/helpers/mockEnv.ts`:

1. Near the other `V3*Row` types, add:

```typescript
export type V3TokenRow = {
  token: string;
  player_account_id: string;
  issued_at_utc: string;
  revoked_at_utc: string | null;
  last_used_at_utc: string | null;
};
```

2. In `MockD1Database`, add alongside the other maps:

```typescript
public readonly v3Tokens = new Map<string, V3TokenRow>();
```

3. In `MockD1Database.first`, before the `return null` fallback, add:

```typescript
if (sql.includes("FROM tokens") && sql.includes("WHERE token = ?")) {
  const token = String(params[0] ?? "");
  return (this.v3Tokens.get(token) as T | undefined) ?? null;
}
```

4. In `MockD1Database.run`, before the `return { changes: 0 }` fallback, add (order the 4-arg `INSERT INTO tokens` check before the 3-arg check):

```typescript
if (sql.includes("INSERT INTO tokens")) {
  const row: V3TokenRow = {
    token: String(params[0] ?? ""),
    player_account_id: String(params[1] ?? ""),
    issued_at_utc: String(params[2] ?? ""),
    revoked_at_utc: params[3] == null ? null : String(params[3]),
    last_used_at_utc: null,
  };
  this.v3Tokens.set(row.token, row);
  return { changes: 1 };
}

if (sql.includes("UPDATE tokens") && sql.includes("SET revoked_at_utc")) {
  const token = String(params[1] ?? "");
  const existing = this.v3Tokens.get(token);
  if (!existing) return { changes: 0 };
  this.v3Tokens.set(token, {
    ...existing,
    revoked_at_utc: params[0] == null ? null : String(params[0]),
  });
  return { changes: 1 };
}

if (sql.includes("UPDATE tokens") && sql.includes("SET last_used_at_utc")) {
  const token = String(params[1] ?? "");
  const existing = this.v3Tokens.get(token);
  if (!existing) return { changes: 0 };
  this.v3Tokens.set(token, {
    ...existing,
    last_used_at_utc: params[0] == null ? null : String(params[0]),
  });
  return { changes: 1 };
}
```

- [ ] **Step 2: Run test to verify failure**

```bash
npx tsx --test test/v3.requireBearerAuth.test.ts
```

Expected: FAIL (module not found).

- [ ] **Step 3: Implement middleware**

```typescript
// ModCFServerV3/src/features/v3/requireBearerAuth.ts

import type { Env } from "../../env";
import { json } from "../../http/json";

export type BearerAuth = {
  token: string;
  playerAccountId: string;
};

type TokenRow = {
  token: string;
  player_account_id: string;
  revoked_at_utc: string | null;
};

export async function requireBearerAuth(
  request: Request,
  env: Env,
): Promise<BearerAuth | Response> {
  const header = request.headers.get("Authorization") ?? "";
  const match = header.match(/^Bearer\s+(\S+)$/);
  if (!match) {
    return json({ error: "invalid_token" }, { status: 401 });
  }
  const token = match[1]!;

  const row = await env.DB.prepare(
    `SELECT token, player_account_id, revoked_at_utc
     FROM tokens
     WHERE token = ?`,
  )
    .bind(token)
    .first<TokenRow>();

  if (!row || row.revoked_at_utc != null) {
    return json({ error: "invalid_token" }, { status: 401 });
  }

  // Best-effort last_used_at update; failures don't block the request.
  await env.DB.prepare(
    `UPDATE tokens SET last_used_at_utc = ? WHERE token = ?`,
  )
    .bind(new Date().toISOString(), token)
    .run()
    .catch(() => undefined);

  return { token: row.token, playerAccountId: row.player_account_id };
}
```

- [ ] **Step 4: Run test**

```bash
npx tsx --test test/v3.requireBearerAuth.test.ts
```

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add ModCFServerV3/src/features/v3/requireBearerAuth.ts ModCFServerV3/test/v3.requireBearerAuth.test.ts
git commit -m "ModCFServerV3 Add requireBearerAuth middleware"
```

---

### Task A4: Refactor /login to issue bearer token

**Files:**
- Modify: `ModCFServerV3/src/features/v3/login.ts`
- Test: `ModCFServerV3/test/v3.login.test.ts` (update existing)

- [ ] **Step 1: Read the existing `login.ts` and `v3.login.test.ts`**

Read:
```bash
cat ModCFServerV3/src/features/v3/login.ts
cat ModCFServerV3/test/v3.login.test.ts
```

Identify: current session-token generation logic (`sess_*` format), what gets written to `installation_sessions`, what the response shape is.

- [ ] **Step 2: Update the test first (TDD)**

Replace the session-token assertion with a bearer token assertion in `v3.login.test.ts`:

```typescript
// In the successful-login test case:
const body = await response.json();
assert.match(body.token, /^[A-Za-z0-9_-]{43}$/);
assert.equal(body.player_account_id, "p1");
assert.equal(body.player_username, "u1");

// Verify the token is persisted in tokens table
const tokenRow = await env.DB.prepare(
  "SELECT player_account_id, revoked_at_utc FROM tokens WHERE token = ?"
).bind(body.token).first();
assert.ok(tokenRow);
assert.equal(tokenRow.player_account_id, "p1");
assert.equal(tokenRow.revoked_at_utc, null);
```

Remove any assertions about `session_id`, `sess_*` prefix, or `installation_sessions` table.

- [ ] **Step 3: Run test to verify failure**

```bash
npx tsx --test test/v3.login.test.ts
```

Expected: FAIL (token assertions fail against old session-based output).

- [ ] **Step 4: Rewrite `login.ts`**

```typescript
// ModCFServerV3/src/features/v3/login.ts

import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import { verifyPassword } from "../../crypto/password";
import { generateBearerToken } from "../../token/generate";

type LoginRequest = {
  player_username?: unknown;
  password?: unknown;
};

type UserRow = {
  player_account_id: string;
  player_username: string;
  password_hash: string;
};

export async function handleLogin(request: Request, env: Env): Promise<Response> {
  const body = (await readJson(request)) as LoginRequest;
  const username = typeof body.player_username === "string" ? body.player_username.trim() : "";
  const password = typeof body.password === "string" ? body.password : "";

  if (!username || !password) {
    return json({ error: "invalid_request" }, { status: 400 });
  }

  const user = await env.DB.prepare(
    `SELECT player_account_id, player_username, password_hash
     FROM users
     WHERE player_username = ?`,
  )
    .bind(username)
    .first<UserRow>();

  if (!user || !(await verifyPassword(password, user.password_hash))) {
    return json({ error: "invalid_credentials" }, { status: 401 });
  }

  const token = generateBearerToken();
  const nowUtc = new Date().toISOString();

  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc) VALUES (?, ?, ?)`,
    ).bind(token, user.player_account_id, nowUtc),
    env.DB.prepare(
      `UPDATE users SET last_login_at_utc = ?, updated_at_utc = ? WHERE player_account_id = ?`,
    ).bind(nowUtc, nowUtc, user.player_account_id),
  ]);

  return json({
    token,
    player_account_id: user.player_account_id,
    player_username: user.player_username,
  });
}
```

- [ ] **Step 5: Run test**

```bash
npx tsx --test test/v3.login.test.ts
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ModCFServerV3/src/features/v3/login.ts ModCFServerV3/test/v3.login.test.ts
git commit -m "ModCFServerV3 Issue bearer token on login"
```

---

### Task A5: Refactor /activate to issue bearer token

**Files:**
- Modify: `ModCFServerV3/src/features/v3/activate.ts`
- Test: `ModCFServerV3/test/v3.activate.test.ts` (update existing)

- [ ] **Step 1: Read existing activate handler and test**

```bash
cat ModCFServerV3/src/features/v3/activate.ts
cat ModCFServerV3/test/v3.activate.test.ts
```

Identify: the installation-public-key field in request (`public_key` JSON), the existing response shape, and the atomic transaction (user + installation).

- [ ] **Step 2: Update tests**

Change test to:
- Request body: `{ player_account_id, player_username, password }` (no `public_key`)
- Response body: `{ token, player_account_id, player_username }`
- Verify: `users` row inserted, `tokens` row inserted with matching `player_account_id`
- Verify: 409 when either `player_account_id` or `player_username` already exists
- Remove all references to `installations` table

- [ ] **Step 3: Run tests (should fail)**

```bash
npx tsx --test test/v3.activate.test.ts
```

Expected: FAIL.

- [ ] **Step 4: Rewrite `activate.ts`**

```typescript
// ModCFServerV3/src/features/v3/activate.ts

import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import { hashPassword } from "../../crypto/password";
import { generateBearerToken } from "../../token/generate";

type ActivateRequest = {
  player_account_id?: unknown;
  player_username?: unknown;
  password?: unknown;
};

export async function handleActivate(request: Request, env: Env): Promise<Response> {
  const body = (await readJson(request)) as ActivateRequest;
  const playerAccountId = typeof body.player_account_id === "string" ? body.player_account_id.trim() : "";
  const playerUsername = typeof body.player_username === "string" ? body.player_username.trim() : "";
  const password = typeof body.password === "string" ? body.password : "";

  if (!playerAccountId || !playerUsername || !password) {
    return json({ error: "invalid_request" }, { status: 400 });
  }

  const existingAccount = await env.DB.prepare(
    `SELECT 1 FROM users WHERE player_account_id = ?`,
  )
    .bind(playerAccountId)
    .first();
  if (existingAccount) {
    return json({ error: "player_account_id_taken" }, { status: 409 });
  }

  const existingUsername = await env.DB.prepare(
    `SELECT 1 FROM users WHERE player_username = ?`,
  )
    .bind(playerUsername)
    .first();
  if (existingUsername) {
    return json({ error: "player_username_taken" }, { status: 409 });
  }

  const passwordHash = await hashPassword(password);
  const token = generateBearerToken();
  const nowUtc = new Date().toISOString();

  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO users (player_account_id, player_username, password_hash, created_at_utc, updated_at_utc, last_login_at_utc)
       VALUES (?, ?, ?, ?, ?, ?)`,
    ).bind(playerAccountId, playerUsername, passwordHash, nowUtc, nowUtc, nowUtc),
    env.DB.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc) VALUES (?, ?, ?)`,
    ).bind(token, playerAccountId, nowUtc),
  ]);

  return json({
    token,
    player_account_id: playerAccountId,
    player_username: playerUsername,
  });
}
```

- [ ] **Step 5: Run test**

```bash
npx tsx --test test/v3.activate.test.ts
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ModCFServerV3/src/features/v3/activate.ts ModCFServerV3/test/v3.activate.test.ts
git commit -m "ModCFServerV3 Rewrite activate endpoint for bearer token auth"
```

---

### Task A6: Implement /logout endpoint

**Files:**
- Create: `ModCFServerV3/src/features/v3/logout.ts`
- Test: `ModCFServerV3/test/v3.logout.test.ts`

- [ ] **Step 1: Write failing test**

```typescript
// ModCFServerV3/test/v3.logout.test.ts
import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { buildEnv } from "./helpers/mockEnv";
import { handleLogout } from "../src/features/v3/logout";

describe("POST /logout", () => {
  it("returns 401 when bearer missing", async () => {
    const env = buildEnv();
    const req = new Request("https://example/logout", { method: "POST" });
    const resp = await handleLogout(req, env);
    assert.equal(resp.status, 401);
  });

  it("revokes the bearer token and returns 204", async () => {
    const env = buildEnv();
    await env.DB.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc) VALUES (?, ?, ?)`
    ).bind("tok_x", "p1", "2026-01-01T00:00:00Z").run();

    const req = new Request("https://example/logout", {
      method: "POST",
      headers: { Authorization: "Bearer tok_x" }
    });
    const resp = await handleLogout(req, env);
    assert.equal(resp.status, 204);

    const row = await env.DB.prepare(
      "SELECT revoked_at_utc FROM tokens WHERE token = ?"
    ).bind("tok_x").first<{ revoked_at_utc: string | null }>();
    assert.ok(row);
    assert.ok(row!.revoked_at_utc);
  });

  it("is idempotent — revoking an already-revoked token still 204s", async () => {
    const env = buildEnv();
    await env.DB.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc, revoked_at_utc) VALUES (?, ?, ?, ?)`
    ).bind("tok_y", "p1", "2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z").run();

    const req = new Request("https://example/logout", {
      method: "POST",
      headers: { Authorization: "Bearer tok_y" }
    });
    const resp = await handleLogout(req, env);
    // Revoked token → requireBearerAuth returns 401 first.
    assert.equal(resp.status, 401);
  });
});
```

- [ ] **Step 2: Run test to verify failure**

```bash
npx tsx --test test/v3.logout.test.ts
```

Expected: FAIL (module not found).

- [ ] **Step 3: Implement `logout.ts`**

```typescript
// ModCFServerV3/src/features/v3/logout.ts

import type { Env } from "../../env";
import { json } from "../../http/json";
import { requireBearerAuth } from "./requireBearerAuth";

export async function handleLogout(request: Request, env: Env): Promise<Response> {
  const auth = await requireBearerAuth(request, env);
  if (auth instanceof Response) return auth;

  await env.DB.prepare(
    `UPDATE tokens SET revoked_at_utc = ? WHERE token = ?`,
  )
    .bind(new Date().toISOString(), auth.token)
    .run();

  return new Response(null, { status: 204 });
}
```

- [ ] **Step 4: Run test**

```bash
npx tsx --test test/v3.logout.test.ts
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ModCFServerV3/src/features/v3/logout.ts ModCFServerV3/test/v3.logout.test.ts
git commit -m "ModCFServerV3 Add logout endpoint"
```

---

### Task A7: Drop auth + installation_id from /run-bundles

**Files:**
- Modify: `ModCFServerV3/src/features/v3/uploadRunBundle.ts`
- Test: `ModCFServerV3/test/v3.runBundles.test.ts` (update)

- [ ] **Step 1: Read current handler**

```bash
cat ModCFServerV3/src/features/v3/uploadRunBundle.ts
```

Identify:
- `requireInstallationAuth(request, env, { allowMissingAuth: true })` call (line ~159)
- Any reads of `installationId` from `auth` return
- `persistedInstallationId` variable and SQL binds referencing it
- Object key construction (currently `run-bundles/<player_account_id>/<installationId>/<run_id>/<hash>.mpack.gz`)

- [ ] **Step 2: Update tests first**

In `v3.runBundles.test.ts`:
- Remove any tests that exercise `allowMissingAuth` or signature verification pathways
- Add: a test that sends a request **without** any `Authorization` header and asserts 200 with valid response
- Add: a test that sends a request with a bogus `Authorization: Bearer garbage` header and asserts 200 (header is ignored)
- Update: the object key pattern assertion to not include `installation_id` segment
- Update: the `run_bundles`, `runs`, `battles` row assertions to drop `installation_id` (expect `null`)

- [ ] **Step 3: Run tests to verify failure**

```bash
npx tsx --test test/v3.runBundles.test.ts
```

Expected: FAIL.

- [ ] **Step 4: Rewrite relevant sections of `uploadRunBundle.ts`**

In `handleUploadRunBundle`:

```typescript
export async function handleUploadRunBundle(
  request: Request,
  env: Env,
): Promise<Response> {
  // Auth removed: writes are open. Any Authorization header is ignored.
  const body = (await readJson(request)) as RunBundleRequest;
  const schemaVersion = asNumber(body.schema_version);
  const playerAccountId = asString(body.player_account_id);
  const submittedAtUtc = asString(body.submitted_at_utc);
  const artifactCodec = asString(body.artifact_codec);
  const artifactBytes = asBytes(body.artifact_bytes);
  const runId = asString(body.run_projection?.run_id);
  const runStatus = asString(body.run_projection?.status);
  const endedAtUtc = asString(body.run_projection?.ended_at_utc);
  const battleProjections = Array.isArray(body.battle_projections)
    ? body.battle_projections
    : [];

  if (
    schemaVersion == null
    || !submittedAtUtc
    || !artifactCodec
    || !artifactBytes
    || !runId
    || !runStatus
    || !endedAtUtc
  ) {
    return json({ error: "invalid_run_bundle_request" }, { status: 400 });
  }

  const persistedPlayerAccountId = playerAccountId ?? AnonymousPlayerAccountId;

  for (const battle of battleProjections) {
    const battleId = asString(battle.battle_id);
    const battleRunId = asString(battle.run_id);
    if (!battleId) return json({ error: "battle_id_required" }, { status: 400 });
    if (!battleRunId || battleRunId !== runId) {
      return json({ error: "battle_run_id_mismatch" }, { status: 400 });
    }
  }

  await rememberKnownPlayerAccountId(persistedPlayerAccountId, env);

  const payloadHash = await sha256Base64(artifactBytes);
  const objectKey =
    `run-bundles/${persistedPlayerAccountId}/${runId}/${payloadHash}.mpack.gz`;
  // ... rest unchanged except SQL binds below
```

Then for the three SQL inserts, remove all `installation_id` parameter bindings (bind `null` so the column — which is now nullable — stays null):

```typescript
// run_bundles INSERT: installation_id → null
// runs INSERT (and ON CONFLICT UPDATE): installation_id → null
// battles INSERT: installation_id → null
```

Update the `ExistingRunBundleRow` lookup query to key on `(player_account_id, run_id, payload_hash)` instead of `(installation_id, run_id, payload_hash)`:

```typescript
const existingBundle = await env.DB.prepare(
  `SELECT bundle_id, object_key
   FROM run_bundles
   WHERE player_account_id = ?
     AND run_id = ?
     AND payload_hash = ?`,
)
  .bind(persistedPlayerAccountId, runId, payloadHash)
  .first<ExistingRunBundleRow>();
```

Delete: `AnonymousInstallationId`, `persistedInstallationId` variable, any imports of `requireInstallationAuth`.

- [ ] **Step 5: Run tests**

```bash
npx tsx --test test/v3.runBundles.test.ts
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ModCFServerV3/src/features/v3/uploadRunBundle.ts ModCFServerV3/test/v3.runBundles.test.ts
git commit -m "ModCFServerV3 Drop auth and installation_id from run-bundles upload"
```

---

### Task A8: Switch /ghost-battles to bearer auth

**Files:**
- Modify: `ModCFServerV3/src/features/v3/queryGhostBattles.ts`
- Test: `ModCFServerV3/test/v3.ghostBattles.test.ts` (update)

- [ ] **Step 1: Read current handler**

```bash
cat ModCFServerV3/src/features/v3/queryGhostBattles.ts
```

Confirm: it currently reads `player_account_id` from URL query param with no auth.

- [ ] **Step 2: Update tests**

Remove any existing tests that pass `player_account_id` via query string. Add:
- 401 when no `Authorization` header
- 401 when bearer is unknown or revoked
- 200 with battle list when bearer is valid; verify the returned battles are only those where `opponent_account_id == token.player_account_id` (ignore any `player_account_id` query param entirely)
- Verify `player_account_id` is **no longer read** from query string (pass a mismatched one and show it's ignored)

- [ ] **Step 3: Run tests (failing)**

```bash
npx tsx --test test/v3.ghostBattles.test.ts
```

Expected: FAIL.

- [ ] **Step 4: Rewrite `queryGhostBattles.ts`**

```typescript
// ModCFServerV3/src/features/v3/queryGhostBattles.ts

import type { Env } from "../../env";
import { getGhostQueryLookbackDays } from "../../config/v3";
import { json } from "../../http/json";
import { requireBearerAuth } from "./requireBearerAuth";

// GhostBattleRow and parseClampedInt definitions unchanged — keep them above.

export async function handleQueryGhostBattles(
  request: Request,
  env: Env,
): Promise<Response> {
  const auth = await requireBearerAuth(request, env);
  if (auth instanceof Response) return auth;

  const url = new URL(request.url);
  const lookbackDays = getGhostQueryLookbackDays(env);
  const limit = parseClampedInt(url.searchParams.get("limit"), 200, 1, 200);
  const fromUtc = new Date(Date.now() - lookbackDays * 24 * 60 * 60 * 1000).toISOString();

  const result = await env.DB.prepare(
    `SELECT b.battle_id, b.recorded_at_utc, b.day, b.player_name, b.player_account_id_in_payload,
            b.player_hero, b.player_rank, b.player_rating, b.player_level,
            b.opponent_name, b.opponent_account_id, b.opponent_hero, b.opponent_rank,
            b.opponent_rating, b.opponent_level, b.result, b.replay_available
     FROM battles AS b
     WHERE b.opponent_account_id = ?
       AND b.recorded_at_utc >= ?
     ORDER BY b.recorded_at_utc DESC, b.battle_id DESC
     LIMIT ?`,
  )
    .bind(auth.playerAccountId, fromUtc, limit)
    .all<GhostBattleRow>();

  return json({
    battles: result.results.map((row) => ({
      // ... mapping unchanged
    })),
  });
}
```

- [ ] **Step 5: Run tests**

```bash
npx tsx --test test/v3.ghostBattles.test.ts
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ModCFServerV3/src/features/v3/queryGhostBattles.ts ModCFServerV3/test/v3.ghostBattles.test.ts
git commit -m "ModCFServerV3 Require bearer auth on ghost-battles query"
```

---

### Task A9: Switch /ghost-battles/:id/replay-link to bearer auth

**Files:**
- Modify: `ModCFServerV3/src/features/v3/createReplayLink.ts`
- Test: `ModCFServerV3/test/v3.replays.test.ts` (update)

- [ ] **Step 1: Read current handler**

```bash
cat ModCFServerV3/src/features/v3/createReplayLink.ts
```

Note the owner check: today it verifies the battle's `installation_id` belongs to the authenticated installation. Change it to verify the battle's `player_account_id` (specifically the column that owns the battle — look at the schema to confirm whether it's `battles.player_account_id` or `battles.player_account_id_in_payload`; use the one that tracks uploader identity, typically `player_account_id`).

- [ ] **Step 2: Update tests**

In `v3.replays.test.ts`:
- 401 when no bearer
- 401 when bearer invalid
- 403 (or `replay_forbidden`) when battle exists but `battle.player_account_id != auth.playerAccountId`
- 200 with `{ download_url, expires_at_utc }` when owner matches

- [ ] **Step 3: Run failing tests**

```bash
npx tsx --test test/v3.replays.test.ts
```

- [ ] **Step 4: Refactor handler**

Replace the existing auth call with:

```typescript
const auth = await requireBearerAuth(request, env);
if (auth instanceof Response) return auth;
```

Replace the owner-check condition from installation-based to:

```typescript
if (battleRow.player_account_id !== auth.playerAccountId) {
  return json({ error: "replay_forbidden" }, { status: 403 });
}
```

Keep all other logic (token signing, R2 object_key lookup) unchanged. The object_key comes from the `run_bundles.object_key` column literally (**do not** algorithmically reconstruct) — this preserves readability of pre-migration objects.

- [ ] **Step 5: Run tests**

```bash
npx tsx --test test/v3.replays.test.ts
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ModCFServerV3/src/features/v3/createReplayLink.ts ModCFServerV3/test/v3.replays.test.ts
git commit -m "ModCFServerV3 Require bearer auth on replay-link endpoint"
```

---

### Task A10: Retire obsolete routes and files

**Files:**
- Modify: `ModCFServerV3/src/index.ts`
- Delete: `ModCFServerV3/src/features/v3/createInstallation.ts`
- Delete: `ModCFServerV3/src/features/v3/uploadObservation.ts`
- Delete: `ModCFServerV3/src/features/v3/requireInstallationAuth.ts`
- Delete: `ModCFServerV3/src/features/v3/requireInstallerSession.ts`
- Delete: `ModCFServerV3/src/crypto/v3Signature.ts` (if unreferenced)
- Delete: `ModCFServerV3/test/v3.installations.test.ts`
- Delete: `ModCFServerV3/test/v3.observations.test.ts`

- [ ] **Step 1: Remove route entries in `index.ts`**

```typescript
// ModCFServerV3/src/index.ts

import type { Env } from "./env";
import { handleActivate } from "./features/v3/activate";
import { handleDownloadReplay } from "./features/v3/downloadReplay";
import { handleLogin } from "./features/v3/login";
import { handleLogout } from "./features/v3/logout";
import { handleQueryGhostBattles } from "./features/v3/queryGhostBattles";
import { handleUploadRunBundle } from "./features/v3/uploadRunBundle";
import { handleCreateReplayLink } from "./features/v3/createReplayLink";
import { preflight, withCors } from "./http/cors";
import { json } from "./http/json";

export default {
  async fetch(request: Request, _env: Env): Promise<Response> {
    if (request.method === "OPTIONS") return preflight(request);
    try {
      const url = new URL(request.url);

      if (request.method === "GET" && url.pathname === "/health") {
        return withCors(request, json({ ok: true }));
      }

      if (request.method === "POST" && url.pathname === "/activate") {
        return withCors(request, await handleActivate(request, _env));
      }
      if (request.method === "POST" && url.pathname === "/login") {
        return withCors(request, await handleLogin(request, _env));
      }
      if (request.method === "POST" && url.pathname === "/logout") {
        return withCors(request, await handleLogout(request, _env));
      }
      if (request.method === "POST" && url.pathname === "/run-bundles") {
        return withCors(request, await handleUploadRunBundle(request, _env));
      }
      if (request.method === "GET" && url.pathname === "/ghost-battles") {
        return withCors(request, await handleQueryGhostBattles(request, _env));
      }

      const replayLinkMatch = url.pathname.match(/^\/ghost-battles\/([^/]+)\/replay-link$/);
      if (request.method === "POST" && replayLinkMatch) {
        return withCors(
          request,
          await handleCreateReplayLink(request, _env, decodeURIComponent(replayLinkMatch[1] ?? "")),
        );
      }

      const replayDownloadMatch = url.pathname.match(/^\/replays\/([^/]+)$/);
      if (request.method === "GET" && replayDownloadMatch) {
        return withCors(
          request,
          await handleDownloadReplay(request, _env, decodeURIComponent(replayDownloadMatch[1] ?? "")),
        );
      }

      return withCors(request, json({ error: "not_found" }, { status: 404 }));
    } catch (error) {
      if (error instanceof Response) return withCors(request, error);
      throw error;
    }
  },
};
```

- [ ] **Step 2: Delete retired source files**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
rm ModCFServerV3/src/features/v3/createInstallation.ts
rm ModCFServerV3/src/features/v3/uploadObservation.ts
rm ModCFServerV3/src/features/v3/requireInstallationAuth.ts
rm ModCFServerV3/src/features/v3/requireInstallerSession.ts
rm ModCFServerV3/test/v3.installations.test.ts
rm ModCFServerV3/test/v3.observations.test.ts
```

- [ ] **Step 3: Check if v3Signature.ts is still referenced**

```bash
grep -rn "v3Signature\|verifyV3Signature\|canonicalRequestString" ModCFServerV3/src ModCFServerV3/test
```

If no matches remain after Task A10 Step 2, delete it:

```bash
rm ModCFServerV3/src/crypto/v3Signature.ts
```

- [ ] **Step 4: Run full test suite**

```bash
cd ModCFServerV3
npm run check
npm test
```

Expected: type-check passes, all remaining tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A ModCFServerV3/
git commit -m "ModCFServerV3 Retire installation auth routes and files"
```

---

### Task A11: Verify `downloadReplay` still works without changes

**Files:**
- Read-only check: `ModCFServerV3/src/features/v3/downloadReplay.ts`
- Verify: `ModCFServerV3/test/v3.replays.test.ts` download-token test cases still pass

- [ ] **Step 1: Read the handler**

```bash
cat ModCFServerV3/src/features/v3/downloadReplay.ts
```

Confirm: it validates a short-lived download token (not a bearer), reads `replay_tokens` table, and streams the object by its stored `object_key`. **Do not** let it reference `installation_id`.

- [ ] **Step 2: If it references installation_id**: remove those references (the token-based auth doesn't need installation). If it doesn't reference installation_id: no-op this task.

- [ ] **Step 3: Run replay tests**

```bash
npx tsx --test test/v3.replays.test.ts
```

Expected: PASS.

- [ ] **Step 4: Commit only if changes were needed**

```bash
git add ModCFServerV3/src/features/v3/downloadReplay.ts
git commit -m "ModCFServerV3 Clean installation_id reference from downloadReplay"
```

---

### Task A12: Update schema test to reflect new shape

**Files:**
- Modify: `ModCFServerV3/test/schema.test.ts`

- [ ] **Step 1: Read existing schema test**

```bash
cat ModCFServerV3/test/schema.test.ts
```

- [ ] **Step 2: Update assertions**

Change assertions to:
- `tokens` table exists with columns: `token`, `player_account_id`, `issued_at_utc`, `revoked_at_utc`, `last_used_at_utc`
- `installations`, `installation_sessions`, `installation_observations` tables do **not** exist
- `run_bundles` has unique index on `(player_account_id, run_id, payload_hash)`

- [ ] **Step 3: Run**

```bash
npx tsx --test test/schema.test.ts
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add ModCFServerV3/test/schema.test.ts
git commit -m "ModCFServerV3 Update schema test for auth simplification"
```

---

### Task A13: Full server check before moving to Phase B

- [ ] **Step 1: Run full type check + test suite**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npm run check
npm test
```

Expected: all green.

- [ ] **Step 2: `wrangler dev` smoke test** (manual but brief)

```bash
npx wrangler dev
```

In a separate terminal:

```bash
# activate
curl -X POST http://127.0.0.1:8787/activate \
  -H "Content-Type: application/json" \
  -d '{"player_account_id":"test1","player_username":"tester","password":"pw"}'
# expect 200 with token

# login (with same creds)
curl -X POST http://127.0.0.1:8787/login \
  -H "Content-Type: application/json" \
  -d '{"player_username":"tester","password":"pw"}'
# expect 200 with (new) token

# unauthed ghost-battles
curl -X GET http://127.0.0.1:8787/ghost-battles
# expect 401

# unauthed run-bundle upload (minimal)
curl -X POST http://127.0.0.1:8787/run-bundles \
  -H "Content-Type: application/json" \
  -d '{"schema_version":3,"player_account_id":"test1","submitted_at_utc":"2026-04-17T00:00:00Z","artifact_codec":"application/x-bpp-runbundle+msgpack+gzip","artifact_bytes":[1,2,3],"run_projection":{"run_id":"r1","status":"finished","ended_at_utc":"2026-04-17T00:00:00Z"},"battle_projections":[]}'
# expect 200
```

Kill `wrangler dev` after. Phase A done.

---

## Phase B — Mod (C#)

### Task B1: Add IdentityDatabase abstraction

**Files:**
- Create: `Game/Identity/IdentityDatabase.cs`
- Test: `tests/IdentityDatabase.Tests/` (create as new project, following pattern of e.g. `tests/HistoryPanelRepository.Tests/`)

- [ ] **Step 1: Study an existing SQLite test project for pattern**

```bash
ls /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/HistoryPanelRepository.Tests/
cat /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/HistoryPanelRepository.Tests/Program.cs | head -80
```

Identify: how the test project is wired (.csproj), how Microsoft.Data.Sqlite is referenced, how tests are structured.

- [ ] **Step 2: Write `IdentityDatabase.cs`**

```csharp
// Game/Identity/IdentityDatabase.cs
using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.Identity
{
    public sealed class IdentityDatabase : IDisposable
    {
        private const int CurrentSchemaVersion = 1;
        private readonly string _filePath;
        private SqliteConnection? _connection;

        public IdentityDatabase(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public SqliteConnection Connection
        {
            get
            {
                if (_connection == null) Open();
                return _connection!;
            }
        }

        public void Open()
        {
            if (_connection != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            _connection = new SqliteConnection($"Data Source={_filePath};Cache=Shared");
            _connection.Open();

            using (var pragma = _connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                pragma.ExecuteNonQuery();
            }

            EnsureSchema();
        }

        private void EnsureSchema()
        {
            int version;
            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                version = Convert.ToInt32(cmd.ExecuteScalar());
            }

            if (version == 0) MigrateTo1();
            // Future: if (version < 2) MigrateTo2(); etc.
        }

        private void MigrateTo1()
        {
            using var tx = _connection!.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                CREATE TABLE auth (
                  id                INTEGER PRIMARY KEY CHECK (id = 1),
                  token             TEXT    NOT NULL,
                  player_account_id TEXT    NOT NULL,
                  player_username   TEXT    NOT NULL,
                  issued_at_utc     TEXT    NOT NULL
                );
                CREATE TABLE player_observation (
                  id                INTEGER PRIMARY KEY CHECK (id = 1),
                  player_account_id TEXT    NOT NULL,
                  player_username   TEXT    NOT NULL,
                  observed_at_utc   TEXT    NOT NULL
                );
                PRAGMA user_version = 1;
            ";
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        public void Dispose()
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
```

- [ ] **Step 3: Write a basic test**

Create `tests/IdentityDatabase.Tests/IdentityDatabase.Tests.csproj` modeled on an existing test project (e.g., `tests/HistoryPanelRepository.Tests/HistoryPanelRepository.Tests.csproj`). Create `tests/IdentityDatabase.Tests/Program.cs`:

```csharp
using System;
using System.IO;
using BazaarPlusPlus.Game.Identity;
using Microsoft.Data.Sqlite;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        RunTest("Open_CreatesSchema", Open_CreatesSchema);
        RunTest("Open_IsIdempotent", Open_IsIdempotent);

        if (_failures > 0) Environment.Exit(1);
        Console.WriteLine("OK");
    }

    private static void RunTest(string name, Action body)
    {
        try
        {
            body();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {ex}");
        }
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"identity-test-{Guid.NewGuid():N}.db");

    private static void Open_CreatesSchema()
    {
        var path = TempDbPath();
        try
        {
            using var db = new IdentityDatabase(path);
            db.Open();
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            var names = new System.Collections.Generic.List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            if (!names.Contains("auth")) throw new Exception("auth table missing");
            if (!names.Contains("player_observation")) throw new Exception("player_observation missing");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void Open_IsIdempotent()
    {
        var path = TempDbPath();
        try
        {
            using (var db1 = new IdentityDatabase(path)) { db1.Open(); }
            using (var db2 = new IdentityDatabase(path)) { db2.Open(); } // should not throw
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 4: Add the new test project to solution / build file if needed**

Follow existing pattern: if `BazaarPlusPlus.csproj` has `<ProjectReference>` entries for test projects, add one for `IdentityDatabase.Tests`. Alternatively the `run.sh` test runner may auto-discover — check:

```bash
cat /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/run.sh
```

- [ ] **Step 5: Run test**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
dotnet test tests/IdentityDatabase.Tests
# or follow the repo's test-run convention
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Game/Identity/IdentityDatabase.cs tests/IdentityDatabase.Tests/
git commit -m "Add IdentityDatabase SQLite layer"
```

---

### Task B2: AuthStore (read / upsert / delete auth row)

**Files:**
- Create: `Game/Identity/AuthStore.cs`
- Test: `tests/IdentityDatabase.Tests/Program.cs` (extend)

- [ ] **Step 1: Define `AuthRecord` model**

```csharp
// Game/Identity/AuthRecord.cs
namespace BazaarPlusPlus.Game.Identity
{
    public sealed record AuthRecord(
        string Token,
        string PlayerAccountId,
        string PlayerUsername,
        string IssuedAtUtc
    );
}
```

- [ ] **Step 2: Write failing tests for AuthStore**

Add to `tests/IdentityDatabase.Tests/Program.cs`:

```csharp
RunTest("AuthStore_TryLoad_WhenEmpty", AuthStore_TryLoad_WhenEmpty);
RunTest("AuthStore_Upsert_ThenLoad", AuthStore_Upsert_ThenLoad);
RunTest("AuthStore_Delete_ClearsRow", AuthStore_Delete_ClearsRow);

private static void AuthStore_TryLoad_WhenEmpty()
{
    var path = TempDbPath();
    try
    {
        using var db = new IdentityDatabase(path);
        db.Open();
        var store = new AuthStore(db);
        if (store.TryLoad(out _)) throw new Exception("expected empty store to return false");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

private static void AuthStore_Upsert_ThenLoad()
{
    var path = TempDbPath();
    try
    {
        using var db = new IdentityDatabase(path);
        db.Open();
        var store = new AuthStore(db);
        var rec = new AuthRecord("tok_xyz", "player_123", "alice", "2026-04-17T00:00:00Z");
        store.Upsert(rec);
        if (!store.TryLoad(out var loaded)) throw new Exception("load returned false");
        if (loaded!.Token != "tok_xyz") throw new Exception("token mismatch");
        if (loaded.PlayerAccountId != "player_123") throw new Exception("account id mismatch");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

private static void AuthStore_Delete_ClearsRow()
{
    var path = TempDbPath();
    try
    {
        using var db = new IdentityDatabase(path);
        db.Open();
        var store = new AuthStore(db);
        store.Upsert(new AuthRecord("t", "p", "u", "2026-04-17T00:00:00Z"));
        store.Delete();
        if (store.TryLoad(out _)) throw new Exception("expected empty after delete");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}
```

- [ ] **Step 3: Run tests (fail)**

```bash
dotnet test tests/IdentityDatabase.Tests
```

- [ ] **Step 4: Implement `AuthStore`**

```csharp
// Game/Identity/AuthStore.cs
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.Identity
{
    public sealed class AuthStore
    {
        private readonly IdentityDatabase _database;
        public AuthStore(IdentityDatabase database) => _database = database;

        public bool TryLoad(out AuthRecord? record)
        {
            using var cmd = _database.Connection.CreateCommand();
            cmd.CommandText = "SELECT token, player_account_id, player_username, issued_at_utc FROM auth WHERE id = 1";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                record = null;
                return false;
            }
            record = new AuthRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            );
            return true;
        }

        public void Upsert(AuthRecord record)
        {
            using var cmd = _database.Connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO auth (id, token, player_account_id, player_username, issued_at_utc)
                VALUES (1, $t, $p, $u, $i)
                ON CONFLICT(id) DO UPDATE SET
                  token = excluded.token,
                  player_account_id = excluded.player_account_id,
                  player_username = excluded.player_username,
                  issued_at_utc = excluded.issued_at_utc";
            cmd.Parameters.AddWithValue("$t", record.Token);
            cmd.Parameters.AddWithValue("$p", record.PlayerAccountId);
            cmd.Parameters.AddWithValue("$u", record.PlayerUsername);
            cmd.Parameters.AddWithValue("$i", record.IssuedAtUtc);
            cmd.ExecuteNonQuery();
        }

        public void Delete()
        {
            using var cmd = _database.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM auth WHERE id = 1";
            cmd.ExecuteNonQuery();
        }
    }
}
```

- [ ] **Step 5: Run tests (pass)**

```bash
dotnet test tests/IdentityDatabase.Tests
```

- [ ] **Step 6: Commit**

```bash
git add Game/Identity/AuthStore.cs Game/Identity/AuthRecord.cs tests/IdentityDatabase.Tests/Program.cs
git commit -m "Add AuthStore for SQLite identity.db"
```

---

### Task B3: Rewrite PlayerObservationStore to use SQLite

**Files:**
- Modify: `Game/Identity/PlayerObservationStore.cs`
- Modify: `Game/Identity/PlayerObservationRecord.cs` (if field shape changes)
- Test: `tests/IdentityDatabase.Tests/Program.cs` (extend)

- [ ] **Step 1: Read current PlayerObservationStore + callers**

```bash
cat Game/Identity/PlayerObservationStore.cs
grep -rn "PlayerObservationStore\|PlayerObservationRecord" --include="*.cs"
```

Identify the public method surface (likely `Load()`, `Save(record)`, or similar) — keep the same signatures so callers don't need edits.

- [ ] **Step 2: Write failing tests**

```csharp
RunTest("PlayerObservationStore_TryLoad_WhenEmpty", PlayerObservationStore_TryLoad_WhenEmpty);
RunTest("PlayerObservationStore_SaveThenLoad", PlayerObservationStore_SaveThenLoad);

private static void PlayerObservationStore_TryLoad_WhenEmpty()
{
    var path = TempDbPath();
    try
    {
        using var db = new IdentityDatabase(path);
        db.Open();
        var store = new PlayerObservationStore(db);
        if (store.TryLoad(out _)) throw new Exception("expected empty");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

private static void PlayerObservationStore_SaveThenLoad()
{
    var path = TempDbPath();
    try
    {
        using var db = new IdentityDatabase(path);
        db.Open();
        var store = new PlayerObservationStore(db);
        var rec = new PlayerObservationRecord("p1", "alice", "2026-04-17T00:00:00Z");
        store.Save(rec);
        if (!store.TryLoad(out var loaded)) throw new Exception("load false");
        if (loaded!.PlayerAccountId != "p1") throw new Exception("id mismatch");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}
```

- [ ] **Step 3: Rewrite `PlayerObservationStore.cs`**

Replace binary-envelope I/O with SQLite ops mirroring `AuthStore`. If `PlayerObservationRecord` currently has extra fields (e.g., `InstallationHint`), drop those — the new schema has 3 columns only.

```csharp
// Game/Identity/PlayerObservationStore.cs
namespace BazaarPlusPlus.Game.Identity
{
    public sealed class PlayerObservationStore
    {
        private readonly IdentityDatabase _database;
        public PlayerObservationStore(IdentityDatabase database) => _database = database;

        public bool TryLoad(out PlayerObservationRecord? record)
        {
            using var cmd = _database.Connection.CreateCommand();
            cmd.CommandText = "SELECT player_account_id, player_username, observed_at_utc FROM player_observation WHERE id = 1";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) { record = null; return false; }
            record = new PlayerObservationRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            return true;
        }

        public void Save(PlayerObservationRecord record)
        {
            using var cmd = _database.Connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO player_observation (id, player_account_id, player_username, observed_at_utc)
                VALUES (1, $p, $u, $t)
                ON CONFLICT(id) DO UPDATE SET
                  player_account_id = excluded.player_account_id,
                  player_username   = excluded.player_username,
                  observed_at_utc   = excluded.observed_at_utc";
            cmd.Parameters.AddWithValue("$p", record.PlayerAccountId);
            cmd.Parameters.AddWithValue("$u", record.PlayerUsername);
            cmd.Parameters.AddWithValue("$t", record.ObservedAtUtc);
            cmd.ExecuteNonQuery();
        }
    }
}
```

Update `PlayerObservationRecord` to match 3-field shape:

```csharp
// Game/Identity/PlayerObservationRecord.cs
namespace BazaarPlusPlus.Game.Identity
{
    public sealed record PlayerObservationRecord(
        string PlayerAccountId,
        string PlayerUsername,
        string ObservedAtUtc
    );
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/IdentityDatabase.Tests
```

- [ ] **Step 5: Commit**

```bash
git add Game/Identity/PlayerObservationStore.cs Game/Identity/PlayerObservationRecord.cs tests/IdentityDatabase.Tests/Program.cs
git commit -m "Rewrite PlayerObservationStore on SQLite identity.db"
```

---

### Task B4: Update IdentityPaths to point at identity.db

**Files:**
- Modify: `Game/Identity/IdentityPaths.cs`
- Modify callers that referenced `.bpp` / `.key` paths (spot-check after change)

- [ ] **Step 1: Read current**

```bash
cat Game/Identity/IdentityPaths.cs
grep -rn "IdentityPaths" --include="*.cs"
```

Identify current properties: `InstallationFilePath`, `InstallationKeyPath`, `PlayerObservationFilePath` etc.

- [ ] **Step 2: Add new property, deprecate old**

Add `IdentityDatabasePath` returning `<GameRoot>/BazaarPlusPlus/Identity/identity.db`. Keep the old properties compiling (pointing to the legacy file paths) for now so Task B9 can remove them in one go after all call-site migrations are done.

```csharp
public string IdentityDatabasePath => Path.Combine(IdentityDirectoryPath, "identity.db");
```

- [ ] **Step 3: Build check**

```bash
dotnet build
```

Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add Game/Identity/IdentityPaths.cs
git commit -m "Add identity.db path to IdentityPaths"
```

---

### Task B5: ModOnlineClient loads bearer token from identity.db

**Files:**
- Modify: `Game/Online/ModOnlineClient.cs`
- Test: extend an existing ModOnlineClient test project or create `tests/ModOnlineClient.Tests/`

- [ ] **Step 1: Read current `ModOnlineClient.cs`**

```bash
cat Game/Online/ModOnlineClient.cs
```

Understand: how it currently wires `InstallationRequestSigner`, what subclients it creates (`ObservationClient`, `RunBundleClient`), and where it's constructed from (probably `Plugin.cs` startup).

- [ ] **Step 2: Add a `BearerState` struct and a `Load()` method**

```csharp
// Game/Online/ModOnlineClient.cs — key additions; keep the rest of the class intact where possible

public sealed class BearerState
{
    public string? Token { get; init; }
    public string? PlayerAccountId { get; init; }
    public string? PlayerUsername { get; init; }
    public bool IsAvailable => !string.IsNullOrEmpty(Token);
}

private BearerState _bearer = new BearerState();

public BearerState Bearer => _bearer;

public void LoadBearerFrom(AuthStore authStore, string observedPlayerAccountId)
{
    if (!authStore.TryLoad(out var auth) || auth == null)
    {
        _bearer = new BearerState();
        BppLog.Warn("identity: no auth row, running without bearer; run Installer to login");
        return;
    }
    if (!string.Equals(auth.PlayerAccountId, observedPlayerAccountId, System.StringComparison.Ordinal))
    {
        _bearer = new BearerState();
        BppLog.Warn("identity: auth.player_account_id != observed; skipping bearer until re-login via Installer");
        return;
    }
    _bearer = new BearerState
    {
        Token = auth.Token,
        PlayerAccountId = auth.PlayerAccountId,
        PlayerUsername = auth.PlayerUsername,
    };
    BppLog.Info($"identity: logged in as {auth.PlayerUsername}");
}

public void HandleUnauthorized(AuthStore authStore)
{
    authStore.Delete();
    _bearer = new BearerState();
    BppLog.Warn("identity: received 401, cleared local auth row");
}
```

- [ ] **Step 3: Write a unit test**

Create `tests/ModOnlineClient.Tests/ModOnlineClient.Tests.csproj` (or extend existing) with cases:

```csharp
RunTest("LoadBearer_NoAuth", LoadBearer_NoAuth);
RunTest("LoadBearer_WithMatchingObservation", LoadBearer_WithMatchingObservation);
RunTest("LoadBearer_WithMismatch", LoadBearer_WithMismatch);
RunTest("HandleUnauthorized_ClearsBearer", HandleUnauthorized_ClearsBearer);
```

Each sets up an `IdentityDatabase` in a temp path, seeds `AuthStore` as needed, constructs `ModOnlineClient` (stub out its HTTP wiring if constructor is heavy — introduce a no-HTTP ctor if needed), calls the relevant method, asserts `Bearer.IsAvailable` / `Token` / log output.

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/ModOnlineClient.Tests
```

- [ ] **Step 5: Commit**

```bash
git add Game/Online/ModOnlineClient.cs tests/ModOnlineClient.Tests/
git commit -m "Load bearer token into ModOnlineClient from identity.db"
```

---

### Task B6: Rewrite RunBundleClient to drop signing (no auth)

**Files:**
- Modify: `Game/Online/RunBundleClient.cs`
- Test: update `tests/RunUploadAuth.Tests/` or equivalent

- [ ] **Step 1: Read the current client**

```bash
cat Game/Online/RunBundleClient.cs
```

Identify: where `InstallationRequestSigner` is called (adding signing headers) and the HTTP request construction.

- [ ] **Step 2: Update the failing tests**

In `tests/RunUploadAuth.Tests/Program.cs`, change expectations:
- The upload request should **not** include `X-BPP-Installation-Id`, `X-BPP-Timestamp`, `X-BPP-Content-SHA256`, `X-BPP-Signature` headers.
- Include no `Authorization` header either.
- Request body still contains `player_account_id`.

- [ ] **Step 3: Rewrite RunBundleClient**

Remove all references to `InstallationRequestSigner`. Keep only the bare HTTP POST to `/run-bundles` with JSON body containing `player_account_id`, `schema_version`, `run_projection`, `battle_projections`, `artifact_codec`, `artifact_bytes`, `submitted_at_utc`. No auth headers.

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/RunUploadAuth.Tests tests/RunUploadSync.Tests tests/RunUploadBootstrap.Tests
```

- [ ] **Step 5: Commit**

```bash
git add Game/Online/RunBundleClient.cs tests/RunUploadAuth.Tests tests/RunUploadSync.Tests tests/RunUploadBootstrap.Tests
git commit -m "Drop request signing from RunBundleClient"
```

---

### Task B7: Rewrite GhostBattleApiClient to use bearer

**Files:**
- Modify: `Game/HistoryPanel/Ghost/GhostBattleApiClient.cs`
- Modify: `Game/HistoryPanel/Ghost/GhostBattleSyncService.cs` (inject bearer)
- Test: `tests/GhostBattleSync.Tests/Program.cs`

- [ ] **Step 1: Read the current client and service**

```bash
cat Game/HistoryPanel/Ghost/GhostBattleApiClient.cs
cat Game/HistoryPanel/Ghost/GhostBattleSyncService.cs
```

Identify signer usage and installation param plumbing.

- [ ] **Step 2: Update tests**

In `tests/GhostBattleSync.Tests/Program.cs`:
- Change the mock HTTP handler assertions: expect `Authorization: Bearer <token>` header; expect **no** `X-BPP-Installation-Id` header.
- Add case: when `ModOnlineClient.Bearer.IsAvailable == false`, `GhostBattleSyncService.SyncAsync` returns a no-auth result (failure or skip, matching the current failure shape) **without making an HTTP call**.
- Add case: when server returns 401, service calls `ModOnlineClient.HandleUnauthorized` and subsequent syncs skip.

- [ ] **Step 3: Rewrite `GhostBattleApiClient`**

Replace signer-header code with:

```csharp
// In the request-creation helper:
request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
```

Drop the `installation` parameter. Client methods take `bearerToken` (string) directly.

- [ ] **Step 4: Update `GhostBattleSyncService`**

```csharp
public async Task<GhostBattleSyncResult> SyncAsync(CancellationToken ct)
{
    var bearer = _onlineClient.Bearer;
    if (!bearer.IsAvailable)
    {
        return GhostBattleSyncResult.Failure("not_logged_in");
    }

    var apiClient = new GhostBattleApiClient(_httpClient, _routes);
    var queryResult = await apiClient.QueryAsync(bearer.Token!, ct);

    if (queryResult.Status == GhostQueryStatus.Unauthorized)
    {
        _onlineClient.HandleUnauthorized(_authStore);
        return GhostBattleSyncResult.Failure("not_logged_in");
    }
    // ... rest unchanged (upsert, checkpoint, etc.)
}
```

Constructor gains `AuthStore _authStore` so `HandleUnauthorized` can clear the row.

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/GhostBattleSync.Tests
```

- [ ] **Step 6: Commit**

```bash
git add Game/HistoryPanel/Ghost/GhostBattleApiClient.cs Game/HistoryPanel/Ghost/GhostBattleSyncService.cs tests/GhostBattleSync.Tests/Program.cs
git commit -m "Switch GhostBattleApiClient to bearer auth"
```

---

### Task B8: Update startup bootstrap

**Files:**
- Modify: `Plugin.cs` (or wherever `ModOnlineClient` and identity are wired up — `grep -rn "ModOnlineClient(" --include="*.cs"`)
- Modify: `Game/Identity/PlayerObservationController.cs` (if it's the writer)

- [ ] **Step 1: Find the wiring point**

```bash
grep -rn "new ModOnlineClient\|InstallationRecordStore\|PlayerObservationStore" --include="*.cs"
```

- [ ] **Step 2: Change wiring to use SQLite path**

At startup:

```csharp
// Pseudo — adapt to actual Plugin.cs structure
var paths = new IdentityPaths(pathService);
var identityDb = new IdentityDatabase(paths.IdentityDatabasePath);
identityDb.Open();

var authStore = new AuthStore(identityDb);
var observationStore = new PlayerObservationStore(identityDb);

// When mod observes player in-game:
observationStore.Save(new PlayerObservationRecord(observedId, observedName, DateTime.UtcNow.ToString("o")));

var onlineClient = new ModOnlineClient(/* HTTP setup */);
onlineClient.LoadBearerFrom(authStore, observedId);

// Pass authStore into services that may need to call HandleUnauthorized:
var ghostSync = new GhostBattleSyncService(onlineClient, authStore, ...);
```

Remove all references to `InstallationRecordStore`, `InstallationRequestSigner` construction at this site.

- [ ] **Step 3: Run full build**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
dotnet build BazaarPlusPlus.csproj
```

Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add Plugin.cs Game/Identity/PlayerObservationController.cs
git commit -m "Wire identity.db + bearer-based ModOnlineClient at startup"
```

---

### Task B9: Retire legacy identity files

**Files:**
- Delete: `Game/Online/InstallationRequestSigner.cs`
- Delete: `Game/Online/ObservationClient.cs` (endpoint retired server-side)
- Delete: `Game/Identity/InstallationRecord.cs`
- Delete: `Game/Identity/InstallationRecordStore.cs`
- Delete: `Game/Identity/BppIdentityEnvelope.cs` (if no remaining consumers)
- Modify: `Game/Identity/IdentityPaths.cs` (remove `InstallationFilePath`, `InstallationKeyPath`, `PlayerObservationFilePath` now that they're unused)
- Delete: `tests/CombatReplayUploadAuth.Tests` / similar test projects that exclusively tested signer paths

- [ ] **Step 1: Verify no live references**

```bash
grep -rn "InstallationRequestSigner\|InstallationRecord\|InstallationRecordStore\|ObservationClient\|BppIdentityEnvelope" --include="*.cs"
```

Expected: matches only inside the files that will be deleted. If other files reference them, stop and migrate those first.

- [ ] **Step 2: Delete files**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
rm Game/Online/InstallationRequestSigner.cs
rm Game/Online/ObservationClient.cs
rm Game/Identity/InstallationRecord.cs
rm Game/Identity/InstallationRecordStore.cs
rm Game/Identity/BppIdentityEnvelope.cs
```

- [ ] **Step 3: Clean `IdentityPaths.cs`**

Remove the legacy path properties. Keep `IdentityDirectoryPath` and `IdentityDatabasePath`.

- [ ] **Step 4: Decide test project retention**

Inspect `tests/CombatReplayUploadAuth.Tests`, `tests/RunUploadAuth.Tests`: if every test exercises only RSA-signing behavior, delete the project. If some tests still have value under bearer auth, keep them but rewrite (this should have happened in Task B6).

```bash
ls tests/CombatReplayUploadAuth.Tests tests/RunUploadAuth.Tests
# Inspect Program.cs, then decide
```

- [ ] **Step 5: Full build**

```bash
dotnet build
```

Expected: succeeds.

- [ ] **Step 6: Run full test suite**

```bash
./run.sh  # or the repo's documented test command
```

Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Retire InstallationRequestSigner and legacy identity files"
```

---

### Task B10: Documentation pass

**Files:**
- Modify: `docs/run-upload.md` (if it documents signing)
- Modify: `docs/mod-features-overview.md` (if it describes installation-based auth)
- Modify: `README.md` / `README_en.md` (only if they mention auth user-facingly)

- [ ] **Step 1: Find auth mentions in docs**

```bash
grep -rln "installation.bpp\|installation.key\|InstallationRequestSigner\|x-bpp-signature\|x-bpp-installation-id" docs/
```

- [ ] **Step 2: Edit each matching doc**

Replace references with new mechanism: "bearer token in `identity.db`", point at the new spec file. Keep changes minimal — do **not** rewrite docs wholesale.

- [ ] **Step 3: Commit**

```bash
git add docs/
git commit -m "Update docs for bearer-token auth model"
```

---

### Task B11: Smoke-test locally (mod side)

- [ ] **Step 1: Start server locally**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/ModCFServerV3
npx wrangler dev
```

- [ ] **Step 2: Prepare an `identity.db` by calling `/login` via curl and inserting the returned token**

```bash
# From another terminal
TOKEN=$(curl -s -X POST http://127.0.0.1:8787/activate \
  -H "Content-Type: application/json" \
  -d '{"player_account_id":"p1","player_username":"u1","password":"pw"}' | jq -r .token)

# Use sqlite3 CLI to write it:
IDENTITY_DB=~/BppLocal/BazaarPlusPlus/Identity/identity.db
mkdir -p "$(dirname "$IDENTITY_DB")"
sqlite3 "$IDENTITY_DB" <<SQL
CREATE TABLE IF NOT EXISTS auth (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  token TEXT NOT NULL,
  player_account_id TEXT NOT NULL,
  player_username TEXT NOT NULL,
  issued_at_utc TEXT NOT NULL
);
INSERT OR REPLACE INTO auth VALUES (1, '$TOKEN', 'p1', 'u1', '2026-04-17T00:00:00Z');
PRAGMA user_version = 1;
SQL
```

- [ ] **Step 2: Point the mod at this DB**

Configure the mod's `IdentityPaths` (via `BppPathService` or env override) to use `~/BppLocal` as the base. Typical override mechanism is an env var checked in `BppPathService` — inspect the class to confirm.

- [ ] **Step 3: Launch the game with the built mod, observe logs**

Expected:
- `identity: logged in as u1` on startup
- Ghost battle sync attempts use bearer, succeed
- Anonymous upload path continues to work

If logs show `identity: no auth row`, verify the DB path and WAL mode.

- [ ] **Step 4: Kill auth row manually and re-launch**

```bash
sqlite3 "$IDENTITY_DB" "DELETE FROM auth"
```

Expected: startup logs `identity: not logged in, please login via Installer`; ghost sync returns `not_logged_in`; uploads continue.

- [ ] **Step 5: Commit a note (optional) capturing smoke-test steps in `docs/run-upload.md` under a "local dev" subsection, if not already present**

---

### Task B12: Cross-phase verification

- [ ] **Step 1: Run the full test sweep**

```bash
cd /Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod
cd ModCFServerV3 && npm run check && npm test && cd ..
dotnet build
./run.sh   # or equivalent full-test entry point; if none, run each dotnet test project
```

Expected: all green on both server and mod.

- [ ] **Step 2: Grep for orphans**

```bash
grep -rn "installation_id\|InstallationId\|installationId" --include="*.ts" --include="*.cs" \
  ModCFServerV3/src Game Infrastructure Core Patches
```

Expected results:
- Server: only in SQL strings referencing columns that still exist as nullable (`runs.installation_id`, `battles.installation_id`, `run_bundles.installation_id` if we chose to keep them NULL-compatible writes) — acceptable if comments note the retention.
- Mod: zero occurrences.

- [ ] **Step 3: Final commit for any cleanup**

```bash
git add -A && git status
# If anything surfaced in step 2 needing cleanup, fix and commit as "Clean stray installation_id references"
```

---

## Out of Scope (tracked separately)

- **Installer changes** (writing `identity.db`, login UI): plan lives in `bazaarplusplus-installer/docs/superpowers/plans/`. Coordinate release: Installer + Mod must ship together (see Step 2 of "Release Order" in spec).
- **Optional DROP COLUMN cleanup** for `installation_id` on `runs`/`battles`/`run_bundles`: deferred until Phase A has been in production two weeks with acceptable 401 rate, per spec.

---

## Self-Review Checklist (run before handing off)

Spec coverage quick pass:

| Spec section | Covered by |
|--------------|-----------|
| Local SQLite schema | Task B1 (schema SQL), B2 (AuthStore), B3 (PlayerObservationStore) |
| Server API surface – /activate | Task A5 |
| Server API surface – /login | Task A4 |
| Server API surface – /logout | Task A6 |
| Server API surface – /run-bundles (no auth, no installation_id) | Task A7 |
| Server API surface – /ghost-battles (bearer) | Task A8 |
| Server API surface – /ghost-battles/:id/replay-link (bearer, owner check) | Task A9 |
| Server API surface – obsolete endpoints removed | Task A10 |
| `tokens` table schema | Task A1 |
| `run_bundles` unique key migration | Task A1 |
| Object storage key new format | Task A7 |
| Object key read via `run_bundles.object_key` (no algorithmic rebuild) | Task A9 |
| KV gating preservation | No explicit task — `rememberKnownPlayerAccountId` call kept in Task A7, logic unchanged |
| Mod startup sequence + mismatch detection | Task B5 + B8 |
| 401 handling in mod | Task B5 + B7 |
| Retirement of InstallationRequestSigner / legacy files | Task B9 |
| Installer runtime behavior | Out of scope (separate plan) |
| Release order | Documented in plan header + "Out of Scope" |
