# BazaarDB Screenshot Upload in Mod (Replacing Installer Integration)

**Status:** Draft for implementation planning
**Date:** 2026-05-24
**Owner:** BazaarPlusPlus mod + ModCFServerV3 + bazaarplusplus-installer

## 1. Background

### 1.1 What "BazaarDB upload" looks like today

The mod itself contains no BazaarDB upload code. End-of-run screenshots are captured by the mod into the `run_screenshots` SQLite table (`capture_source = 'end_of_run_auto'`), and a separate process — the [bazaarplusplus-installer](../../../../bazaarplusplus-installer) Tauri app — owns the BazaarDB integration:

- [auto_watcher.rs](../../../../bazaarplusplus-installer/src-tauri/src/bazaardb/auto_watcher.rs) polls the mod's `run_screenshots` table for new rows and enqueues them for upload.
- [worker.rs](../../../../bazaarplusplus-installer/src-tauri/src/bazaardb/worker.rs) runs a background queue with retry/backoff.
- [client.rs](../../../../bazaarplusplus-installer/src-tauri/src/bazaardb/client.rs) POSTs each screenshot as multipart form to `https://bazaardb.bazaarplusplus.com/api/uploads/screenshot`.
- [keyring.rs](../../../../bazaarplusplus-installer/src-tauri/src/bazaardb/keyring.rs) holds a per-user BazaarDB API token in the OS keychain.
- A Svelte UI in `bazaarplusplus-installer/src/lib/bazaardb/` and `src/routes/settings/+page.svelte` exposes connect/disconnect and per-screenshot manual upload.

The Installer is, in effect, a long-running middleman between the mod's local SQLite and BazaarDB's HTTP API. This couples three concerns that should be independent: the mod's data plane, the installer's installation/update plane, and BazaarDB's ingestion plane.

### 1.2 What the mod already does that's relevant

- Screenshot capture: [EndOfRunScreenshotController](../../../Game/Screenshots/EndOfRunScreenshotController.cs), [RunScreenshotSqliteStore](../../../Game/Screenshots/Persistence/RunScreenshotSqliteStore.cs), [RunScreenshotMetadataReader](../../../Game/Screenshots/RunScreenshotMetadataReader.cs), [EndOfRunScreenshotPatch](../../../Patches/EndOfRun/EndOfRunScreenshotPatch.cs).
- Run-bundle upload to `mod-api-v3.bazaarplusplus.com`: [RunBundleUploadService](../../../Game/RunLogging/Upload/RunBundleUploadService.cs), [RunBundleUploadStore](../../../Game/RunLogging/Upload/RunBundleUploadStore.cs), [RunBundleUploadController](../../../Game/RunLogging/Upload/RunUploadController.cs), [RunBundleClient](../../../Game/Online/RunBundleClient.cs), [V3Routes](../../../Game/Online/V3Routes.cs), [V3UploadDefaults](../../../Game/Online/V3UploadDefaults.cs). The HTTP client and routes are constructed once in [Plugin.cs](../../../Plugin.cs) and shared.
- Settings catalog + toggles: [BppSettingsDockCatalog](../../../Game/Settings/BppSettingsDockCatalog.cs), [BppSettingsDockDefinition](../../../Game/Settings/BppSettingsDockDefinition.cs), [SettingsMenuToggleBridge](../../../Game/Settings/SettingsMenuToggleBridge.cs); example label resolver [NameOverride.SettingsMenuLabel.cs](../../../Game/NameOverride/NameOverride.SettingsMenuLabel.cs).
- Config persistence: [IBppConfig](../../../Core/Config/IBppConfig.cs), [BppConfig](../../../Core/Config/BppConfig.cs) (BepInEx `ConfigEntry<T>` pattern).

### 1.3 ModCFServerV3

The mod's server target is a Cloudflare Worker in [ModCFServerV3/](../../../ModCFServerV3/), with D1 (SQLite) for state and `wrangler.toml` for bindings. It already accepts `POST /run-bundles` and exposes `V3Routes` for the mod. Adding new routes, a new D1 table, and a new R2 binding is the natural way to extend it.

## 2. Goals

1. The mod uploads end-of-run screenshots + structured run summary directly to ModCFServerV3, gated by a new user setting (default off).
2. BazaarDB pulls daily from a stable, authenticated endpoint on ModCFServerV3 — no direct connection between the mod (or installer) and BazaarDB.
3. The Installer's BazaarDB code is deleted in full. The Installer becomes a pure install/update tool again.
4. The change is consistent with the existing `RunBundleUpload` pipeline in the mod (same shapes, same retry semantics, same composition style) so reviewers can read it once and understand both.
5. Past screenshots upload retroactively when the user enables the toggle.

## 3. Non-goals

- No backfill of screenshots that were uploaded directly to BazaarDB by the legacy Installer path. If BazaarDB wants those, they handle it on their side as a one-time data move.
- No mod-side UI for per-screenshot upload status. The toggle plus log lines are the only feedback.
- No mod-side UI to delete or "forget" individual screenshots.
- No manifest pagination implementation in v1 (contract is specified; deferred until volume warrants).
- No daily tarball/zip packaging. The "daily archive" is a query result, not a built artifact.

## 4. Architecture

Three independent units. No runtime cross-talk between them.

```
┌─────────────────────────────────────┐
│ Mod  (BazaarPlusPlus.dll)           │
│  ┌─────────────────────────────┐    │
│  │ Existing run-bundle upload  │── POST /run-bundles ───────────┐
│  └─────────────────────────────┘                                 │
│  ┌─────────────────────────────┐                                 │
│  │ NEW screenshot upload       │── POST /bazaardb-screenshots ──┐│
│  │ (gated by Settings toggle)  │                                ││
│  └─────────────────────────────┘                                ││
└─────────────────────────────────────┘                           ││
                                                                  ▼▼
                                                  ┌──────────────────────────┐
                                                  │ ModCFServerV3 (Worker)   │
                                                  │  Ingest → R2 + D1        │
                                                  │  Manifest (D1 read)      │◄── BazaarDB GET (bearer)
                                                  │  Image proxy (R2 stream) │◄── BazaarDB GET (bearer)
                                                  └──────────────────────────┘

┌─────────────────────────────────────┐
│ Installer (Tauri)                   │
│  ✂ bazaardb/* deleted               │  ← no longer participates
│  ✂ Tauri bazaardb commands deleted  │
│  ✂ Svelte BazaarDB UI deleted       │
└─────────────────────────────────────┘
```

Boundaries:

- The mod's existing run-bundle uploader is untouched. The new uploader shares only the `HttpClient` instance and `V3Routes` base URL.
- BazaarDB sits outside the trust boundary: every endpoint it calls requires a bearer token, it never touches D1 directly, and image URLs are Worker-proxied so we can revoke/rate-limit later without changing the URL contract.
- The Installer's only responsibility going forward is install/update.

## 5. Mod-side components

### 5.1 New files

Mirror the existing `Game/RunLogging/Upload/` shape one-for-one:

```
Game/Screenshots/Upload/
├── BazaarDbScreenshotUploadStore.cs          ← sidecar SQLite sync state
├── BazaarDbScreenshotUploadService.cs        ← pick N pending, build payload, call client, record result
├── BazaarDbScreenshotUploadController.cs     ← MonoBehaviour: startup wiring, periodic tick, on-toggle handler
├── BazaarDbScreenshotUploadPayload.cs        ← in-memory snapshot built before serialization
└── BazaarDbUpload.SettingsMenuLabel.cs       ← localized label resolver

Game/Online/
├── BazaarDbScreenshotClient.cs               ← HttpClient wrapper, POST + result struct
└── Models/BazaarDbScreenshotUploadRequestV3.cs ← JSON DTO (matches server contract)
```

### 5.2 Config

Extend [IBppConfig.cs](../../../Core/Config/IBppConfig.cs) and [BppConfig.cs](../../../Core/Config/BppConfig.cs):

- `BazaarDbUploadEnabled : ConfigEntry<bool>` — section `"BazaarDB"`, key `"UploadScreenshots"`, default `false`.
- Description string: *"When enabled, end-of-run screenshots and their summary (hero, days, MMR, rank, position, etc.) are uploaded to our server and forwarded to BazaarDB. Includes screenshots from past runs. You can turn this off at any time; we will stop uploading and never delete what was already sent."*

### 5.3 SQLite sync state

Add a sidecar table to the existing mod SQLite database (the one that already holds `run_screenshots`). Do not add columns to `run_screenshots` itself — sidecar keeps the feature removable:

```sql
CREATE TABLE bazaardb_screenshot_uploads (
  screenshot_id          TEXT PRIMARY KEY REFERENCES run_screenshots(screenshot_id),
  status                 TEXT NOT NULL,           -- 'pending' | 'uploaded' | 'permanent_failure'
  attempts               INTEGER NOT NULL DEFAULT 0,
  last_attempted_at_utc  TEXT,
  last_error             TEXT,
  uploaded_at_utc        TEXT
);
```

Backfill on startup: any `run_screenshots` row with `capture_source = 'end_of_run_auto'` that has no sidecar row gets one inserted with `status = 'pending'`. That single rule makes "toggle-flip-on uploads past screenshots" work without any cutoff-timestamp logic.

### 5.4 Cadence and wiring

Reuse `StartupUploadAttemptRunner` (20s startup delay, 180s interval) — the same construction the run-bundle uploader uses. The controller short-circuits the tick if `Config.BazaarDbUploadEnabled.Value` is `false` (no DB read, no network).

Composition (in [Plugin.cs](../../../Plugin.cs) / [BppComposition.cs](../../../BppComposition.cs)):

1. Construct `BazaarDbScreenshotClient` with the shared `HttpClient` + `V3Routes`.
2. Construct `BazaarDbScreenshotUploadStore` against the existing SQLite connection factory.
3. Construct `BazaarDbScreenshotUploadService` from store + client + identity provider (`player_account_id` resolver, same one the run-bundle uploader uses).
4. Construct `BazaarDbScreenshotUploadController` and add it as a `MonoBehaviour` to the plugin GameObject, parallel to `RunUploadController`.

## 6. Settings menu integration

### 6.1 Catalog entry

Add to [BppSettingsDockCatalog.cs](../../../Game/Settings/BppSettingsDockCatalog.cs) (`Definitions` list):

```csharp
new(
    "BazaarDbUpload",
    BazaarDbUpload.SettingsMenuLabel.Resolve,
    new SettingsMenuToggleBridge(
        readValue:  () => Plugin.Instance?.Config?.BazaarDbUploadEnabled?.Value ?? false,
        writeValue: v => { if (Plugin.Instance?.Config?.BazaarDbUploadEnabled is { } e) e.Value = v; },
        onChanged:  v => BazaarDbScreenshotUploadController.OnEnabledChanged(v))
)
```

### 6.2 Label resolver

New file `Game/Screenshots/Upload/BazaarDbUpload.SettingsMenuLabel.cs` matching the shape of [NameOverride.SettingsMenuLabel.cs](../../../Game/NameOverride/NameOverride.SettingsMenuLabel.cs). Strings:

- en: `"Upload screenshots to BazaarDB"`
- zh: `"上传截图到 BazaarDB"`

Match coverage of any other languages used elsewhere in the project.

### 6.3 Toggle semantics

| Transition | Effect |
|---|---|
| `false → true`  | `OnEnabledChanged(true)` arms an immediate upload tick (skips the 180s wait). All pending sidecar rows — including backfilled past ones — are picked up. |
| `true  → false` | Sets an in-memory flag the controller checks at the top of every tick. In-flight POSTs finish. No data is deleted. Re-enabling later resumes from where it stopped. |

## 7. Server-side components (ModCFServerV3)

### 7.1 Routes

| Method | Path                                      | Auth                                | Purpose                                |
|--------|-------------------------------------------|-------------------------------------|----------------------------------------|
| POST   | `/bazaardb-screenshots`                   | `player_account_id` in body         | Mod ingest                             |
| GET    | `/bazaardb/manifest?date=YYYY-MM-DD`      | `Authorization: Bearer <token>`     | BazaarDB list-for-day                  |
| GET    | `/bazaardb/image/{screenshot_id}`         | `Authorization: Bearer <token>`     | BazaarDB image fetch (Worker proxies R2) |

### 7.2 Ingest handler — `POST /bazaardb-screenshots`

Request body (`BazaarDbScreenshotUploadRequestV3`):

```json
{
  "schema_version": 1,
  "submitted_at_utc": "2026-05-24T12:34:56.789Z",
  "player_account_id": "acct-9",
  "screenshot_id": "snap-1",
  "run_id": "run-77",
  "hero_name": "Mak",
  "final_days": 14,
  "final_victories": 10,
  "player_name": "Xinyu",
  "player_rank": "Diamond",
  "player_rating": 1942,
  "player_position": 1,
  "captured_at_utc": "2026-05-23T20:30:05+00:00",
  "image_format": "jpeg",
  "image_bytes_base64": "<...>"
}
```

`Content-Type: application/json`. JPEG bytes are base64 in the body — keeps the mod's existing JSON pipeline (Newtonsoft.Json + `application/json`) unchanged. Per-screenshot size is small (~200KB–1MB).

Validation: schema_version, required fields, `captured_at_utc` parses to a date, JPEG magic bytes on the decoded image.

Side effects (R2 first, then D1 upsert):

1. `R2.put('bazaardb/screenshots/{YYYY-MM-DD}/{screenshot_id}.jpg', imageBytes, { httpMetadata: { contentType: 'image/jpeg' } })` — date = `captured_at_utc.toUTCDate()`.
2. D1 upsert:

```sql
INSERT INTO bazaardb_screenshots (
  screenshot_id, player_account_id, run_id, hero_name, final_days,
  final_victories, player_name, player_rank, player_rating, player_position,
  captured_at_utc, captured_date_utc, image_format, image_sha256, image_bytes,
  r2_key, uploaded_at_utc, schema_version
) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
ON CONFLICT(screenshot_id) DO UPDATE SET
  uploaded_at_utc = excluded.uploaded_at_utc,
  image_sha256    = excluded.image_sha256,
  image_bytes     = excluded.image_bytes,
  r2_key          = excluded.r2_key;
```

Single-row, primary-key UPSERT — 1 row read + 1 row written per call. Cheapest possible mutation in D1; passes the "scan shape" check in [CLAUDE.md](../../../CLAUDE.md).

Response:

- 200 `{ "status": "ok", "screenshot_id": "...", "uploaded_at_utc": "..." }` on success (including idempotent re-POST of the same id).
- 400 `{ "status": "rejected", "reason": "..." }` on validation failure. Mod marks `permanent_failure`.
- 5xx on Worker / R2 / D1 errors. Mod retries on next tick.

Note: no 409 on hash mismatch. A `screenshot_id` collision with different content is essentially impossible (ids are generated at capture from a UUID); if it ever happened, "newest upload wins" is the right outcome and the upsert handles it.

Orphan handling: if R2 put succeeds but D1 upsert fails, the R2 object is orphaned. Acceptable for v1 — the same `screenshot_id` will re-upload and overwrite the same deterministic R2 key. Revisit if storage cost shows accumulating orphans.

### 7.3 Manifest handler — `GET /bazaardb/manifest?date=YYYY-MM-DD`

- Auth: constant-time compare against `BAZAARDB_PULL_TOKEN` secret. 401 on missing / wrong, with no body details.
- Validates `date` (`YYYY-MM-DD`, UTC, not in the future).
- D1 read: `SELECT * FROM bazaardb_screenshots WHERE captured_date_utc = ? ORDER BY uploaded_at_utc ASC`. Covered by `idx_bazaardb_screenshots_date(captured_date_utc, uploaded_at_utc)` — index range scan, not table scan.
- Empty result returns `{ ..., "items": [] }` with 200, not 404. Lets BazaarDB call any date without conditional logic.

Response shape:

```json
{
  "date": "2026-05-23",
  "schema_version": 1,
  "generated_at_utc": "2026-05-24T03:00:01Z",
  "items": [
    {
      "screenshot_id": "...",
      "run_id": "...",
      "hero_name": "...",
      "final_days": 14,
      "final_victories": 10,
      "player_name": "...",
      "player_account_id": "...",
      "player_rank": "Diamond",
      "player_rating": 1942,
      "player_position": 1,
      "captured_at_utc": "...",
      "image_url": "https://mod-api-v3.bazaarplusplus.com/bazaardb/image/{screenshot_id}",
      "image_format": "jpeg",
      "image_sha256": "...",
      "image_bytes": 184293
    }
  ]
}
```

BazaarDB must send the same `Authorization: Bearer <BAZAARDB_PULL_TOKEN>` header on `image_url` GETs as on the manifest GET — the image proxy (Section 7.4) authenticates against the same secret.

Pagination (contract only — implementation deferred): `?cursor=<uploaded_at_utc>&limit=N` returns items strictly after `cursor` sorted ascending; response includes `next_cursor` when truncated.

### 7.4 Image proxy — `GET /bazaardb/image/{screenshot_id}`

- Auth: same bearer token.
- Look up `r2_key` in D1, stream the R2 object through with correct `Content-Type` and `Content-Length`, set `Cache-Control: private, max-age=86400`.
- 404 on missing key.

Why proxy instead of signed R2 URL: URL stays stable, revocation is just a secret rotation, no R2 credential leaks to BazaarDB.

### 7.5 D1 schema (new migration)

```sql
CREATE TABLE bazaardb_screenshots (
  screenshot_id        TEXT PRIMARY KEY,
  player_account_id    TEXT NOT NULL,
  run_id               TEXT,
  hero_name            TEXT,
  final_days           INTEGER,
  final_victories      INTEGER,
  player_name          TEXT,
  player_rank          TEXT,
  player_rating        INTEGER,
  player_position      INTEGER,
  captured_at_utc      TEXT NOT NULL,
  captured_date_utc    TEXT NOT NULL,    -- 'YYYY-MM-DD' UTC, denormalized for index
  image_format         TEXT NOT NULL,
  image_sha256         TEXT NOT NULL,
  image_bytes          INTEGER NOT NULL,
  r2_key               TEXT NOT NULL,
  uploaded_at_utc      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
  schema_version       INTEGER NOT NULL
);
CREATE INDEX idx_bazaardb_screenshots_date
  ON bazaardb_screenshots(captured_date_utc, uploaded_at_utc);
```

### 7.6 R2 layout

- New R2 binding `bazaardb-assets` configured in [ModCFServerV3/wrangler.toml](../../../ModCFServerV3/wrangler.toml).
- Key pattern: `bazaardb/screenshots/{YYYY-MM-DD}/{screenshot_id}.jpg` where the date is the UTC capture date. Date-prefixed keys make lifecycle / cleanup operations cheap (`list-prefix bazaardb/screenshots/2026-03-`).

### 7.7 Secrets

- `BAZAARDB_PULL_TOKEN` — bearer token that BazaarDB presents on manifest + image calls. Set out-of-band via `wrangler secret put BAZAARDB_PULL_TOKEN`. Token value is held in operator notes, **not in this repo**.

## 8. Data flow end-to-end

1. **Capture** (unchanged). End-of-run reveal fires → screenshot captured → row inserted into `run_screenshots` with `capture_source = 'end_of_run_auto'`.
2. **Sidecar enqueue**. At the same insert site (and via a startup backfill scan), `BazaarDbScreenshotUploadStore.EnsureEnqueued(screenshotId)` runs `INSERT OR IGNORE INTO bazaardb_screenshot_uploads (...)`. Idempotent.
3. **Upload tick** (every 180s, plus on-demand after toggle-on or run completion). Controller short-circuits if the gate is off. Otherwise: select up to 3 pending rows (`SELECT … WHERE status = 'pending' ORDER BY screenshot_id LIMIT 3` — single-process mod, no concurrency, no lease needed; same shape as `RunBundleUploadStore`), for each one build a snapshot from `run_screenshots` + metadata + JPEG bytes, build the JSON payload, call `BazaarDbScreenshotClient.UploadAsync`. Record the result.
4. **Worker ingest** (Section 7.2). Validates → R2 PUT → D1 UPSERT → 200.
5. **BazaarDB pull** (daily, scheduled by BazaarDB side). `GET /bazaardb/manifest?date=YYYY-MM-DD` with bearer token, then iterate `items[]` and `GET item.image_url` (same token).
6. **End of mod's responsibility**. Sidecar row stays as the dedupe ledger so the mod never re-uploads the same screenshot. Original JPEG on disk is untouched; existing screenshot retention rules apply.

## 9. Installer cleanup

### 9.1 Files deleted outright

```
bazaarplusplus-installer/src-tauri/src/bazaardb/
├── auto_watcher.rs
├── backoff.rs
├── client.rs
├── deeplink.rs
├── endpoints.rs
├── image_pipeline.rs
├── keyring.rs
├── mod.rs
├── payload.rs
├── queue.rs
└── worker.rs

bazaarplusplus-installer/src-tauri/src/commands/bazaardb.rs

bazaarplusplus-installer/src/lib/bazaardb/
├── account-store.ts
├── account-store.test.ts
├── api.ts
└── upload-actions.ts
```

### 9.2 Files pruned (selective edits, file kept)

| File | Change |
|---|---|
| `src-tauri/src/lib.rs` | Remove `mod bazaardb;`, the `bazaardb::{…}` use-list, `spawn_worker(app.handle().clone())`, the deeplink branch that calls `commands::bazaardb::connect_bazaardb`, and the four invoke handlers (`connect_bazaardb`, `disconnect_bazaardb`, `get_bazaardb_status`, `upload_screenshot_to_bazaardb`). |
| `src-tauri/src/commands/mod.rs` | Remove `pub mod bazaardb;`. |
| `src-tauri/src/installer_db/mod.rs` | Drop the `CREATE TABLE bazaardb_settings` block and its accessors. Add a one-shot migration `DROP TABLE IF EXISTS bazaardb_settings;` at installer startup so existing users' DBs lose the orphan table (no data worth keeping — it mirrored the keyring). |
| `src/lib/bridge/commands.ts` | Remove the four typed command entries. |
| `src/lib/config/endpoints.ts` | Remove `BAZAARDB_BASE_URL`. |
| `src/routes/settings/+page.svelte` | Remove the `import { accountStore } from '$lib/bazaardb/account-store'` and any UI bound to it (the BazaarDB connect/disconnect section). |
| `src/lib/components/stream/StreamRecordLibrary.svelte` | Remove the `import { uploadScreenshot }` and `UploadStatus` import and the per-row "Upload to BazaarDB" button/state. |

### 9.3 Kept untouched

- `src/lib/about/content.ts` — references `https://bazaardb.gg` as a project link, not an upload path.
- `src-tauri/src/installer_db/` itself — has other tables for installer state.

### 9.4 Side-effects of removal

- After `cargo build`, prune now-unused crates from `Cargo.toml` (e.g. `keyring`, multipart features of `reqwest` if no other consumer). Same on the frontend side for `package.json`.
- OS keychain entries written by the deleted `keyring.rs` linger until the user clears them manually. Acceptable — inert without code reading them. Note in release notes.

## 10. Error handling

### 10.1 Mod side

Three failure tiers, mirroring `RunBundleUploadService`:

| Class | Examples | Store action | Retry? |
|---|---|---|---|
| Transient | network timeout, 5xx, 429, DNS failure, image file missing-but-likely-temporary | `attempts++`, `last_error` set, status stays `pending` | Yes, next tick |
| Permanent 4xx | 400 schema mismatch, 401/403 (shouldn't happen here, but treat as permanent), 413 | `status = 'permanent_failure'`, `last_error` set | No |
| Build-time | screenshot JPEG file deleted from disk, sidecar row for a `screenshot_id` that no longer exists in `run_screenshots` | `status = 'permanent_failure'`, `last_error = "build_snapshot_failed:<reason>"` | No |

No per-row exponential backoff. Cadence is the 180s tick — same as the run-bundle uploader. If a row exceeds 20 attempts and is still `pending`, log a warning but keep retrying.

### 10.2 Server side

- Validation failure → 400 with `{ status: 'rejected', reason }`.
- R2 or D1 write failure → 500. Mod retries.
- R2 write succeeded then D1 failed → orphaned R2 object; tolerated (Section 7.2).
- Manifest with no rows for date → 200 with empty `items`.
- Manifest auth fail → 401, no body details.

## 11. Testing

### 11.1 Mod side

In the existing test project structure (`tests/RunScreenshotSqliteStore.Tests/`, `tests/RunLoggingSqliteSchema.Tests/`):

- `BazaarDbScreenshotUploadStore.Tests` — sidecar enqueue idempotency, select-pending ordering and limit, mark-uploaded / permanent_failure / record-attempt, backfill scan inserts rows for orphaned `run_screenshots`.
- `BazaarDbScreenshotUploadService.Tests` — happy path, build-failure → permanent_failure, transient HTTP error → `attempts++`, permanent 4xx → permanent_failure, batch size respected, toggle-off short-circuit.
- No test for `BazaarDbScreenshotUploadController` (MonoBehaviour wiring). Mirrors the project's lack of test for `RunUploadController`. Per [CLAUDE.md](../../../CLAUDE.md): *"If there is no meaningful automated test seam for a change, it is acceptable to ship without adding a new test."*

### 11.2 Server side

In `ModCFServerV3/test/`:

- Ingest handler: valid payload → 200 + D1 row + R2 put; idempotent re-POST → 200 no extra row; invalid schema_version → 400; missing required field → 400.
- Manifest handler: bearer token check, date validation, empty result shape, ordering, pagination contract (cursor accepted even if not heavily exercised yet).
- Image proxy: bearer check, 404 on missing key, correct content-type pass-through.

### 11.3 Installer side

No new tests; deletion only. Run existing `cargo test` and frontend test suites after pruning to catch lingering imports.

## 12. Rollout sequence

Ship in this order to avoid a gap where neither path uploads:

1. **Server first** (`ModCFServerV3`): deploy the three new routes + D1 migration + R2 binding. Set `BAZAARDB_PULL_TOKEN`. Endpoints live but quiet.
2. **Mod release**: ship the new uploader with the setting off by default. Users who don't toggle are unaffected.
3. **BazaarDB side**: BazaarDB team configures their cron against `/bazaardb/manifest` + the image URLs with the pull token. They can begin pulling immediately; manifests are empty until users opt in.
4. **Installer release**: remove the Installer's bazaardb code only after the mod release has rolled out widely enough to be confident users have migrated. A grace period of 1–2 mod releases is conservative; faster is fine if mod adoption is fast.

Swapping steps 3 and 4 is harmless — BazaarDB sees fewer screenshots during the gap because Installer users haven't updated yet, but no data is lost.

## 13. Open items

- Whether to lower the default tick interval (currently 180s, matching run-bundle) since screenshot payloads are small and the user has just opted in to uploading. Default stays at 180s for v1; revisit if users report visible lag after enabling.
- Whether to expose `attempts` / `last_error` in some debug command for support. Skipped in v1.
- Cleanup job for orphaned R2 objects from failed-mid-write ingests. Skipped in v1.

## 14. Summary

The mod gains a parallel uploader (mirroring `RunBundleUpload` one-for-one) that posts each end-of-run screenshot plus its summary fields as JSON to `POST /bazaardb-screenshots` on `ModCFServerV3`. The Worker writes JPEG bytes to R2 (`bazaardb/screenshots/YYYY-MM-DD/{id}.jpg`) and a row to D1 (`bazaardb_screenshots`). BazaarDB pulls daily via a bearer-token-protected `GET /bazaardb/manifest?date=YYYY-MM-DD` and follows the per-item image URL (Worker-proxied from R2). A new settings toggle (`BazaarDbUploadEnabled`, default off) gates the mod uploader; flipping it on backfills past screenshots. The Installer's BazaarDB code — auto_watcher, queue, client, keyring, Tauri commands, Svelte UI — is deleted in full.
