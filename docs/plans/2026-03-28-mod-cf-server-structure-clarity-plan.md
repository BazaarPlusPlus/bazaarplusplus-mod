# ModCFServer Structure Clarity Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor `ModCFServer/` into a clearer Cloudflare Worker project structure without changing request/response behavior or deployment semantics.

**Architecture:** Keep `ModCFServer/` as a standalone Worker project inside this repo, but split the current monolithic worker into route wiring, feature handlers, crypto/auth helpers, and persistence modules. Preserve the current endpoints, D1/R2 bindings, and test behavior while making file ownership explicit and keeping `src/index.ts` thin.

**Tech Stack:** Cloudflare Workers TypeScript, Wrangler, D1, R2, Node test runner via `tsx --test`

---

## Target Structure

```text
ModCFServer/
  package.json
  tsconfig.json
  wrangler.toml
  src/
    index.ts
    env.ts
    types/
      api.ts
      db.ts
    http/
      json.ts
      request.ts
    crypto/
      base64.ts
      hash.ts
      signature.ts
    persistence/
      schema.ts
      clients.ts
      nonces.ts
      runUploads.ts
      replayUploads.ts
    features/
      registerClient.ts
      uploadRun.ts
      uploadReplay.ts
  test/
    helpers/
      mockEnv.ts
      crypto.ts
    registerClient.test.ts
    uploadRun.test.ts
    uploadReplay.test.ts
    authValidation.test.ts
```

## Constraints

- Do not move `ModCFServer/` out of the repo in this refactor.
- Do not change the deployed route contract:
  - `POST /health`
  - `POST /clients/register`
  - `POST /runs/upload`
  - `POST /replays/upload`
- Do not change current D1 table names or R2 object-key semantics unless a test proves the change is intentional.
- Keep docs minimal. Do not add a README unless a concrete workflow remains unclear after the refactor.

## Verification Commands

- `npm run check --prefix ModCFServer`
- `npm test --prefix ModCFServer`

### Task 1: Freeze current worker behavior with test split

**Files:**
- Modify: `ModCFServer/test/index.test.ts`
- Create: `ModCFServer/test/helpers/mockEnv.ts`
- Create: `ModCFServer/test/helpers/crypto.ts`
- Create: `ModCFServer/test/registerClient.test.ts`
- Create: `ModCFServer/test/uploadRun.test.ts`
- Create: `ModCFServer/test/uploadReplay.test.ts`
- Create: `ModCFServer/test/authValidation.test.ts`

**Step 1: Move reusable test helpers into helper modules**

- Extract the in-memory D1 and R2 mocks from `ModCFServer/test/index.test.ts` into `ModCFServer/test/helpers/mockEnv.ts`.
- Extract test-only key generation, body hashing, and canonical-signing helpers into `ModCFServer/test/helpers/crypto.ts`.

**Step 2: Split behavior tests by endpoint or responsibility**

- Move registration assertions into `ModCFServer/test/registerClient.test.ts`.
- Move replay upload happy-path and idempotency assertions into `ModCFServer/test/uploadReplay.test.ts`.
- Move run upload assertions into `ModCFServer/test/uploadRun.test.ts`.
- Move signature, nonce, timestamp, and body-hash rejection cases into `ModCFServer/test/authValidation.test.ts`.

**Step 3: Keep a single import surface**

- Each split test file should import the worker from `ModCFServer/src/index.ts`.
- Remove duplicated inline mocks from the test files after helper extraction.

**Step 4: Run tests to verify no behavior changed**

Run:

```bash
npm test --prefix ModCFServer
```

Expected: all Worker tests pass with the same behaviors now expressed in smaller files.

**Step 5: Commit**

```bash
git add ModCFServer/test
git commit -m "Refactor ModCFServer tests by feature"
```

### Task 2: Introduce shared runtime and type modules

**Files:**
- Create: `ModCFServer/src/env.ts`
- Create: `ModCFServer/src/types/api.ts`
- Create: `ModCFServer/src/types/db.ts`
- Modify: `ModCFServer/src/index.ts`

**Step 1: Move environment shape into one module**

- Extract the `Env` interface from `ModCFServer/src/index.ts` into `ModCFServer/src/env.ts`.
- Re-export only the binding types needed by feature and persistence modules.

**Step 2: Move request and row types into dedicated files**

- Put request DTOs like `RegisterRequest` into `ModCFServer/src/types/api.ts`.
- Put DB row types like `RegisteredClientRow` into `ModCFServer/src/types/db.ts`.

**Step 3: Update imports without changing behavior**

- Replace local type declarations in `ModCFServer/src/index.ts` with imports from `env.ts` and `types/`.

**Step 4: Run typecheck**

Run:

```bash
npm run check --prefix ModCFServer
```

Expected: typecheck passes with no behavior changes.

**Step 5: Commit**

```bash
git add ModCFServer/src
git commit -m "Refactor ModCFServer shared types"
```

### Task 3: Extract pure HTTP and crypto helpers

**Files:**
- Create: `ModCFServer/src/http/json.ts`
- Create: `ModCFServer/src/http/request.ts`
- Create: `ModCFServer/src/crypto/base64.ts`
- Create: `ModCFServer/src/crypto/hash.ts`
- Create: `ModCFServer/src/crypto/signature.ts`
- Modify: `ModCFServer/src/index.ts`

**Step 1: Move response and request parsing helpers**

- Extract `json`, `readJson`, `trimString`, `normalizePurpose`, and `absolutePath` into `http/`.
- Keep them pure and free of D1/R2 dependencies.

**Step 2: Move canonicalization and hash helpers**

- Extract base64 conversion helpers and `sha256Base64` into `crypto/`.
- Extract canonical request construction and RSA verification into `crypto/signature.ts`.

**Step 3: Keep function boundaries narrow**

- `http/` should only deal with request/response formatting.
- `crypto/` should only deal with bytes, hashes, canonical strings, and signature verification.

**Step 4: Run tests and typecheck**

Run:

```bash
npm run check --prefix ModCFServer
npm test --prefix ModCFServer
```

Expected: no behavior regressions after helper extraction.

**Step 5: Commit**

```bash
git add ModCFServer/src
git commit -m "Refactor ModCFServer HTTP and crypto helpers"
```

### Task 4: Extract persistence modules and schema ownership

**Files:**
- Create: `ModCFServer/src/persistence/schema.ts`
- Create: `ModCFServer/src/persistence/clients.ts`
- Create: `ModCFServer/src/persistence/nonces.ts`
- Create: `ModCFServer/src/persistence/runUploads.ts`
- Create: `ModCFServer/src/persistence/replayUploads.ts`
- Modify: `ModCFServer/src/index.ts`

**Step 1: Move schema bootstrap out of the entrypoint**

- Extract `ensureSchema` into `ModCFServer/src/persistence/schema.ts`.
- Keep the full D1 schema definition there so table ownership is discoverable in one place.

**Step 2: Split D1 access by table ownership**

- `clients.ts`: register and fetch clients.
- `nonces.ts`: lookup and persist request nonces.
- `runUploads.ts`: write run upload rows.
- `replayUploads.ts`: write replay upload rows.

**Step 3: Preserve SQL and contract semantics**

- Do not rename tables or columns.
- Keep the current `ON CONFLICT` behavior unless a test proves a bug.

**Step 4: Run verification**

Run:

```bash
npm run check --prefix ModCFServer
npm test --prefix ModCFServer
```

Expected: worker behavior remains unchanged after moving SQL ownership.

**Step 5: Commit**

```bash
git add ModCFServer/src
git commit -m "Refactor ModCFServer persistence modules"
```

### Task 5: Extract feature handlers

**Files:**
- Create: `ModCFServer/src/features/registerClient.ts`
- Create: `ModCFServer/src/features/uploadRun.ts`
- Create: `ModCFServer/src/features/uploadReplay.ts`
- Modify: `ModCFServer/src/index.ts`

**Step 1: Create one handler per endpoint**

- Move `/clients/register` logic to `features/registerClient.ts`.
- Move `/runs/upload` logic to `features/uploadRun.ts`.
- Move `/replays/upload` logic to `features/uploadReplay.ts`.

**Step 2: Keep auth orchestration shared**

- Any shared verified-client logic should live in a private helper near the feature modules or in a dedicated auth helper if it is used by both upload handlers.
- Avoid leaving request-signing logic embedded in `index.ts`.

**Step 3: Reduce `src/index.ts` to routing only**

- `ModCFServer/src/index.ts` should:
  - import `ensureSchema`
  - import the three handlers
  - dispatch by `request.method` and pathname
  - return `not_found` for unsupported routes

**Step 4: Run verification**

Run:

```bash
npm run check --prefix ModCFServer
npm test --prefix ModCFServer
```

Expected: route wiring remains correct and the tests still pass.

**Step 5: Commit**

```bash
git add ModCFServer/src
git commit -m "Refactor ModCFServer feature handlers"
```

### Task 6: Make test typechecking explicit

**Files:**
- Modify: `ModCFServer/tsconfig.json`
- Optional Create: `ModCFServer/tsconfig.test.json`
- Modify: `ModCFServer/package.json`

**Step 1: Decide on one of two patterns**

- Preferred: expand `ModCFServer/tsconfig.json` `include` to cover both `src/**/*.ts` and `test/**/*.ts`.
- Alternative: add `ModCFServer/tsconfig.test.json` and a dedicated `check:test` script.

**Step 2: Keep `npm run check` obvious**

- Prefer one command that typechecks what developers actually edit.
- If a second script is needed, name it explicitly and wire it into local verification guidance.

**Step 3: Run typecheck and tests**

Run:

```bash
npm run check --prefix ModCFServer
npm test --prefix ModCFServer
```

Expected: tests are now included in type-level validation without changing runtime behavior.

**Step 4: Commit**

```bash
git add ModCFServer/package.json ModCFServer/tsconfig.json ModCFServer/tsconfig.test.json
git commit -m "Refactor ModCFServer typecheck coverage"
```

### Task 7: Clarify project boundaries and local noise

**Files:**
- Modify: `ModCFServer/.gitignore`
- Optional Modify: `ModCFServer/package.json`
- Optional Modify: `ModCFServer/wrangler.toml`

**Step 1: Make ignored local artifacts explicit**

- Keep `node_modules/` ignored.
- Add any actual generated local artifacts if they appear during development, but do not add speculative entries.

**Step 2: Keep scripts intention-revealing**

- If helpful, rename or add scripts only when they clarify workflow, for example:
  - `check`
  - `test`
  - `dev`
  - `deploy`
- Do not add a script sprawl.

**Step 3: Only annotate `wrangler.toml` where ambiguity remains**

- Add short comments only if binding purpose is still unclear after the refactor.
- Do not turn `wrangler.toml` into prose documentation.

**Step 4: Run verification**

Run:

```bash
npm run check --prefix ModCFServer
npm test --prefix ModCFServer
```

Expected: no behavior changes; local workflow is clearer.

**Step 5: Commit**

```bash
git add ModCFServer/.gitignore ModCFServer/package.json ModCFServer/wrangler.toml
git commit -m "Refine ModCFServer project boundaries"
```

### Task 8: Final structure review and regression pass

**Files:**
- Review: `ModCFServer/src/index.ts`
- Review: `ModCFServer/src/features/*.ts`
- Review: `ModCFServer/src/persistence/*.ts`
- Review: `ModCFServer/test/**/*.test.ts`

**Step 1: Check entrypoint thinness**

- Verify `ModCFServer/src/index.ts` contains only route wiring and schema bootstrap orchestration.
- If it still holds endpoint logic or SQL, move that code into the appropriate module.

**Step 2: Check ownership boundaries**

- `features/` should coordinate.
- `persistence/` should own SQL.
- `crypto/` should own canonicalization and signature verification.
- `test/helpers/` should own mocks and signing helpers.

**Step 3: Run final verification**

Run:

```bash
npm run check --prefix ModCFServer
npm test --prefix ModCFServer
```

Expected: all checks pass on the final structure.

**Step 4: Commit**

```bash
git add ModCFServer
git commit -m "Finalize ModCFServer structure cleanup"
```

## Notes for the Implementer

- This is a structure refactor, not a protocol redesign.
- Prefer moving existing code with minimal edits before attempting cleanup.
- After each extraction, run verification immediately instead of batching risk.
- If a helper ends up with only one caller and no reuse, inline it back out. Do not build a utility graveyard.
