# BazaarDB Screenshot Upload in Mod Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move BazaarDB screenshot upload out of the bazaarplusplus-installer and into the mod itself, mirroring the existing `RunBundleUpload` pipeline. The mod posts screenshots + run summary JSON to `ModCFServerV3`, which writes them to D1 + R2. BazaarDB pulls daily from a bearer-token-protected manifest. The installer's BazaarDB code is deleted in full.

**Architecture:** Three independent units with no runtime cross-talk: (1) the **mod** gains a parallel uploader under `Game/Screenshots/Upload/` that shares the existing `HttpClient` + `V3Routes` and is gated by a new `BazaarDbUploadEnabled` setting (default off); (2) **`ModCFServerV3`** gains three new routes (`POST /bazaardb-screenshots`, `GET /bazaardb/manifest`, `GET /bazaardb/image/{id}`) backed by a new D1 table and a new R2 binding; (3) the **bazaarplusplus-installer** has its entire `bazaardb/` subtree, Tauri commands, and Svelte UI deleted. Past screenshots upload retroactively the moment the user flips the toggle on, driven by an `INSERT OR IGNORE` backfill scan into a sidecar SQLite table.

**Tech Stack:** C# (BepInEx mod, .NET Standard 2.1) + Microsoft.Data.Sqlite + Newtonsoft.Json (Unity-friendly); TypeScript (Cloudflare Worker) + D1 (SQLite) + R2 + Vitest; Rust (Tauri installer being pruned). The plan touches three repos: `bazaarplusplus-mod/` (primary), `bazaarplusplus-mod/ModCFServerV3/` (sibling subdir), and `../bazaarplusplus-installer/` (sibling repo).

---

## Background: Important Adaptations from the Design Doc

Three deviations from [the design](../specs/2026-05-24-bazaardb-upload-in-mod-design.md) were discovered while reading existing code. Each is justified inline below; they are baked into every task in this plan.

1. **Image format is PNG, not JPEG.** [`ScreenshotService.WriteCurrentFrameToFile`](../../../Game/Screenshots/ScreenshotService.cs) calls `texture.EncodeToPNG()` and writes `.png` files via [`ScreenshotPathBuilder`](../../../Game/Screenshots/ScreenshotPathBuilder.cs). Converting to JPEG at upload time would require a re-encode (Unity's `EncodeToJPG` is available but adds CPU + a quality knob to bikeshed). For v1 we ship PNG end-to-end: `image_format = "png"`, R2 key suffix `.png`, `Content-Type: image/png`, magic-byte check uses the PNG signature `89 50 4E 47 0D 0A 1A 0A`. The "200KB–1MB" size estimate in the design becomes ~1–3MB at 1080p, still well under any practical Worker request-size limit (100MB).

2. **Sidecar enqueue is a backfill-only model — no insert-time hook.** The design says "at the same insert site (and via a startup backfill scan)". Wiring a hook into [`EndOfRunScreenshotController`](../../../Game/Screenshots/EndOfRunScreenshotController.cs) couples the existing screenshot infra to the new uploader. A single `INSERT OR IGNORE INTO bazaardb_screenshot_uploads (...) SELECT ... FROM run_screenshots LEFT JOIN ... WHERE sidecar.screenshot_id IS NULL` query, run at the top of every upload cycle, subsumes both startup backfill and per-screenshot enqueue (the freshly-saved row is picked up on the next 180s tick — or sooner if the run-ended event arms an immediate attempt). One mechanism, zero coupling.

3. **Sidecar table lives in `RunLogSqliteSchema.BootstrapSql`.** [`RunLogSqliteSchema`](../../../Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs) is a single bootstrap SQL string with `CREATE TABLE IF NOT EXISTS` semantics — there's no migration scaffolding. Adding the sidecar table to this string is the established pattern; bump `LocalDatabaseSchemaVersion` from 12 → 13 for documentation. The `bazaardb_screenshot_uploads` table is **NOT** in its own migration file; it's a single block inside `BootstrapSql`.

---

## File Structure

### Mod side (new files)

| File | Responsibility |
|---|---|
| `Game/Screenshots/Upload/BazaarDbScreenshotUploadStore.cs` | SQLite operations: sidecar enqueue/backfill, select-pending, mark-uploaded, mark-failed, mark-permanent-failure, build snapshot from `run_screenshots` + `bazaardb_screenshot_uploads` + image bytes. |
| `Game/Screenshots/Upload/BazaarDbScreenshotUploadService.cs` | Cycle orchestrator. Resolves player account id; loops pending rows; builds payload; calls client; records result. Owns `HttpClient`. |
| `Game/Screenshots/Upload/BazaarDbScreenshotUploadController.cs` | MonoBehaviour: startup wiring, periodic tick via `StartupUploadAttemptRunner`, on-toggle handler. Short-circuits if disabled. Static `OnEnabledChanged(bool)`. |
| `Game/Screenshots/Upload/BazaarDbScreenshotUploadSnapshot.cs` | In-memory snapshot built by store, consumed by service. |
| `Game/Screenshots/Upload/BazaarDbScreenshotUploadSettingsMenuLabel.cs` | Localized label resolver (en/zh + other languages present in the codebase). |
| `Game/Online/BazaarDbScreenshotClient.cs` | HTTP POST wrapper around the JSON DTO; returns `BazaarDbScreenshotUploadResult { Succeeded, Error, Permanent }`. |
| `Game/Online/Models/BazaarDbScreenshotUploadRequestV3.cs` | JSON DTO with `[JsonProperty]` snake_case attributes. |
| `tests/BazaarDbScreenshotUploadStore.Tests/` | Test project mirroring `tests/RunScreenshotSqliteStore.Tests/` structure. |
| `tests/BazaarDbScreenshotUploadService.Tests/` | Test project for happy/sad service paths with a fake client. |

### Mod side (modified files)

| File | Change |
|---|---|
| `Core/Config/IBppConfig.cs` | Add `ConfigEntry<bool>? BazaarDbUploadEnabled { get; }`. |
| `Core/Config/BppConfig.cs` | Add property + `config.Bind("BazaarDB", "UploadScreenshots", false, ...)` in `Initialize`. |
| `Game/Online/V3Routes.cs` | Add three new properties: `UploadBazaarDbScreenshot`, `BazaarDbManifest`, `BazaarDbImageBase`. |
| `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs` | Bump version to 13; add `bazaardb_screenshot_uploads` `CREATE TABLE` to `BootstrapSql`. |
| `Game/Settings/BppSettingsDockCatalog.cs` | New definition entry for `BazaarDbUpload` toggle. |
| `Plugin.cs` | Construct & attach `BazaarDbScreenshotUploadController`; tear down in `DetachRuntimeComponents`. |

### Server side (`ModCFServerV3/`)

| File | Responsibility |
|---|---|
| `migrations/0012_create_bazaardb_screenshots.sql` | New D1 table + indexes. |
| `src/features/v3/uploadBazaarDbScreenshot.ts` | `POST /bazaardb-screenshots` handler: validate, R2 put, D1 upsert. |
| `src/features/v3/getBazaarDbManifest.ts` | `GET /bazaardb/manifest?date=YYYY-MM-DD` handler with bearer auth. |
| `src/features/v3/getBazaarDbImage.ts` | `GET /bazaardb/image/{screenshot_id}` handler — proxies R2. |
| `src/env.ts` | Add `BAZAARDB_BUCKET: R2Bucket` and `BAZAARDB_PULL_TOKEN: string`. |
| `test/env.d.ts` | Mirror new env fields for tests. |
| `src/index.ts` | Register 3 new routes (1 static POST, 2 regex GETs). |
| `wrangler.toml` | Add `[[r2_buckets]]` binding `BAZAARDB_BUCKET`; `BAZAARDB_PULL_TOKEN` set via `wrangler secret put` out-of-band. |
| `test/v3.bazaardbScreenshots.test.ts` | Vitest coverage for all three handlers. |
| `test/helpers/seed.ts` | Extend `resetTestState` to wipe `bazaardb_screenshots` + new R2 bucket. |

### Installer (sibling repo `../bazaarplusplus-installer/`)

| File | Change |
|---|---|
| `src-tauri/src/bazaardb/*` (11 files) | Delete the entire directory. |
| `src-tauri/src/commands/bazaardb.rs` | Delete. |
| `src-tauri/src/lib.rs` | Remove `mod bazaardb;`, use-list entries, `spawn_worker`, the deeplink branch, the 7 invoke handlers. |
| `src-tauri/src/commands/mod.rs` | Remove `pub mod bazaardb;`. |
| `src-tauri/src/installer_db/mod.rs` | Drop `CREATE TABLE bazaardb_settings`, the `get_setting`/`set_setting` accessors; add a one-shot `DROP TABLE IF EXISTS bazaardb_settings` for cleanup. |
| `src/lib/bazaardb/*` (4 files) | Delete. |
| `src/lib/bridge/commands.ts` | Remove the 7 typed command entries. |
| `src/lib/config/endpoints.ts` | Remove `BAZAARDB_BASE_URL`, `BAZAARDB_TOKEN_PAGE_URL`. |
| `src/routes/settings/+page.svelte` | Remove the `accountStore` import, the import handlers, the entire "BazaarDB" UI section, the `call('get_auto_upload_enabled')` / `call('list_pending_uploads')` calls. |
| `src/lib/components/stream/StreamRecordLibrary.svelte` | Remove `uploadScreenshot`/`UploadStatus` imports, `uploadStatuses` state, `handleUpload` function, the per-row upload button + status UI. |
| `src-tauri/Cargo.toml` | Remove `keyring = "3"` line; remove `"multipart"` from the `reqwest` feature list (verify no other consumer). |

---

## Self-Review Notes

- **Spec coverage**: All 14 sections of the design have at least one task. Section 11.3 (installer side has no new tests, just delete & build) is covered by Task 24's `cargo build` + `npm test` verification.
- **Type consistency**: `BazaarDbScreenshotUploadResult` carries `Succeeded`, `Error`, `Permanent` (used throughout Tasks 8–10). `BazaarDbScreenshotUploadSnapshot` has `ScreenshotId` + `Payload` (Tasks 6–9). `BazaarDbScreenshotUploadRequestV3` has the exact JSON property names used by the server handler in Task 13.
- **Open items from design §13**: deliberately left out — `attempts/last_error` debug command, orphaned-R2 cleanup, lower tick interval — all v2 work.

---

## Task Index

| # | Task | Phase |
|---|---|---|
| 1 | Add sidecar table to `RunLogSqliteSchema.BootstrapSql`, bump schema version | Mod schema |
| 2 | Add `BazaarDbUploadEnabled` config entry | Mod config |
| 3 | Add new route properties to `V3Routes` | Mod routing |
| 4 | Create `BazaarDbScreenshotUploadRequestV3` DTO | Mod DTO |
| 5 | Create `BazaarDbScreenshotUploadSnapshot` | Mod data |
| 6 | Create `BazaarDbScreenshotUploadStore` (with TDD) | Mod store |
| 7 | Create `BazaarDbScreenshotClient` | Mod HTTP |
| 8 | Create `BazaarDbScreenshotUploadService` (with TDD) | Mod service |
| 9 | Create `BazaarDbScreenshotUploadController` | Mod controller |
| 10 | Wire controller into `Plugin.cs` | Mod composition |
| 11 | Create label resolver + register settings dock entry | Mod settings |
| 12 | Add D1 migration `0012_create_bazaardb_screenshots.sql` | Server schema |
| 13 | Add R2 binding + secret env to `wrangler.toml` + `env.ts` + `env.d.ts` | Server config |
| 14 | Implement `uploadBazaarDbScreenshot` handler (TDD) | Server ingest |
| 15 | Implement `getBazaarDbManifest` handler (TDD) | Server manifest |
| 16 | Implement `getBazaarDbImage` handler (TDD) | Server proxy |
| 17 | Register the three new routes in `src/index.ts` | Server router |
| 18 | Extend `test/helpers/seed.ts` `resetTestState` | Server tests |
| 19 | Run mod + server test suites; fix issues | Mod+server verify |
| 20 | Delete installer `bazaardb/` subtree + Tauri command file + frontend `bazaardb/` dir | Installer purge |
| 21 | Prune installer `lib.rs` + `commands/mod.rs` + `installer_db/mod.rs` | Installer pruning |
| 22 | Prune installer frontend (`commands.ts`, `endpoints.ts`, `+page.svelte`, `StreamRecordLibrary.svelte`) | Installer pruning |
| 23 | Prune installer `Cargo.toml` (keyring + reqwest/multipart) | Installer deps |
| 24 | Verify installer builds (`cargo build`, `npm test`) | Installer verify |
| 25 | Run mod `BuildAll` + final cross-repo smoke | Final verify |

---

## Phase 1 — Mod-side schema + config

### Task 1: Add `bazaardb_screenshot_uploads` sidecar table to schema bootstrap

**Files:**
- Modify: [`Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs`](../../../Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs)

- [ ] **Step 1.1: Bump schema version constant**

In [`RunLogSqliteSchema.cs:10`](../../../Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs):

```csharp
public static int LocalDatabaseSchemaVersion => 13;
```

- [ ] **Step 1.2: Add new table-name constant**

Add this property near the other `*TableName` properties (e.g. after `RunSyncStateTableName` on line 34):

```csharp
public static string BazaarDbScreenshotUploadsTableName => "bazaardb_screenshot_uploads";
```

- [ ] **Step 1.3: Add `CREATE TABLE` block to `BootstrapSql`**

In `BootstrapSql` (lines 48–234), add this block immediately **after** the `{RunSyncStateTableName}` CREATE TABLE (around line 199), but **before** the `CREATE INDEX` block. The sidecar references `run_screenshots(screenshot_id)` with `ON DELETE CASCADE` so cleanup is automatic if a screenshot row is ever deleted:

```sql
CREATE TABLE IF NOT EXISTS {BazaarDbScreenshotUploadsTableName} (
    screenshot_id          TEXT PRIMARY KEY,
    status                 TEXT NOT NULL,
    attempts               INTEGER NOT NULL DEFAULT 0,
    last_attempted_at_utc  TEXT NULL,
    last_error             TEXT NULL,
    uploaded_at_utc        TEXT NULL,
    FOREIGN KEY (screenshot_id) REFERENCES {RunScreenshotsTableName}(screenshot_id) ON DELETE CASCADE
);
```

Also append this index after the existing `idx_{RunScreenshotsTableName}_*` indexes:

```sql
CREATE INDEX IF NOT EXISTS idx_{BazaarDbScreenshotUploadsTableName}_status
    ON {BazaarDbScreenshotUploadsTableName}(status);
```

- [ ] **Step 1.4: Verify the schema change compiles**

Run: `dotnet build /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: build succeeds with no errors. Schema is interpolation-string only — no behavioral change beyond CREATE-IF-NOT-EXISTS.

- [ ] **Step 1.5: Commit**

```bash
git add Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs
git commit -m "$(cat <<'EOF'
Add bazaardb_screenshot_uploads sidecar table

Sidecar enables tracking BazaarDB upload state per end-of-run screenshot
without touching the existing run_screenshots schema. CASCADE delete on
screenshot removal keeps the ledger self-cleaning.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Add `BazaarDbUploadEnabled` config entry

**Files:**
- Modify: [`Core/Config/IBppConfig.cs`](../../../Core/Config/IBppConfig.cs)
- Modify: [`Core/Config/BppConfig.cs`](../../../Core/Config/BppConfig.cs)

- [ ] **Step 2.1: Add interface property**

Append to `IBppConfig` (after line 50, before the closing brace):

```csharp
    ConfigEntry<bool>? BazaarDbUploadEnabled { get; }
```

- [ ] **Step 2.2: Add implementation property**

Append to `BppConfig` properties (after `CombatReplayVideoMaxQueuedFrames` on line 54):

```csharp
    public ConfigEntry<bool>? BazaarDbUploadEnabled { get; private set; }
```

- [ ] **Step 2.3: Bind in `Initialize`**

Append at the end of `Initialize` (after the last `CombatReplayVideoMaxQueuedFrames` bind on line 187, before the closing brace):

```csharp
        // BazaarDB
        BazaarDbUploadEnabled = config.Bind(
            "BazaarDB",
            "UploadScreenshots",
            false,
            "When enabled, end-of-run screenshots and their summary (hero, days, MMR, rank, position, etc.) are uploaded to our server and forwarded to BazaarDB. Includes screenshots from past runs. You can turn this off at any time; we will stop uploading and never delete what was already sent."
        );
```

- [ ] **Step 2.4: Verify compile**

Run: `dotnet build /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: build succeeds.

- [ ] **Step 2.5: Commit**

```bash
git add Core/Config/IBppConfig.cs Core/Config/BppConfig.cs
git commit -m "$(cat <<'EOF'
Add BazaarDbUploadEnabled config entry

Default off so the new uploader is opt-in. Description matches the user-
facing copy in the design doc so the .cfg file is self-documenting.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Add the three new route properties to `V3Routes`

**Files:**
- Modify: [`Game/Online/V3Routes.cs`](../../../Game/Online/V3Routes.cs)

- [ ] **Step 3.1: Add new properties and the image-URL builder method**

Replace the constructor (lines 8–13) and add the new properties + method right after the existing `DownloadReplay` method:

```csharp
    private V3Routes(Uri apiBaseUri)
    {
        ApiBaseUri = apiBaseUri;
        UploadRunBundle = BuildAbsolute("/run-bundles");
        QueryGhostBattles = BuildAbsolute("/ghost-battles");
        UploadBazaarDbScreenshot = BuildAbsolute("/bazaardb-screenshots");
        BazaarDbManifestBase = BuildAbsolute("/bazaardb/manifest");
    }

    public Uri ApiBaseUri { get; }

    public string UploadRunBundle { get; }

    public string QueryGhostBattles { get; }

    public string UploadBazaarDbScreenshot { get; }

    public string BazaarDbManifestBase { get; }
```

(The image URL is a per-screenshot path built server-side and exposed in manifest responses; the mod doesn't need a builder for it. The manifest endpoint also only needs a base URL — query string is appended by the BazaarDB puller, not the mod.)

Keep all existing properties and methods (`CreateReplayLink`, `DownloadReplay`, `TryCreate`, `BuildAbsolute`) unchanged.

- [ ] **Step 3.2: Verify compile**

Run: `dotnet build /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: build succeeds.

- [ ] **Step 3.3: Commit**

```bash
git add Game/Online/V3Routes.cs
git commit -m "$(cat <<'EOF'
Add BazaarDB screenshot upload route to V3Routes

Mod only needs the upload route. Manifest + image routes are server-side
contracts (the BazaarDB puller hits them, not the mod).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 2 — Mod-side data layer

### Task 4: Create `BazaarDbScreenshotUploadRequestV3` DTO

**Files:**
- Create: `Game/Online/Models/BazaarDbScreenshotUploadRequestV3.cs`

- [ ] **Step 4.1: Write the file**

Content of `Game/Online/Models/BazaarDbScreenshotUploadRequestV3.cs`:

```csharp
#nullable enable
using System;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Online.Models;

internal sealed class BazaarDbScreenshotUploadRequestV3
{
    [JsonProperty("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonProperty("submitted_at_utc")]
    public string SubmittedAtUtc { get; set; } = string.Empty;

    [JsonProperty("player_account_id")]
    public string PlayerAccountId { get; set; } = string.Empty;

    [JsonProperty("screenshot_id")]
    public string ScreenshotId { get; set; } = string.Empty;

    [JsonProperty("run_id")]
    public string? RunId { get; set; }

    [JsonProperty("hero_name")]
    public string? HeroName { get; set; }

    [JsonProperty("final_days")]
    public int? FinalDays { get; set; }

    [JsonProperty("final_victories")]
    public int? FinalVictories { get; set; }

    [JsonProperty("player_name")]
    public string? PlayerName { get; set; }

    [JsonProperty("player_rank")]
    public string? PlayerRank { get; set; }

    [JsonProperty("player_rating")]
    public int? PlayerRating { get; set; }

    [JsonProperty("player_position")]
    public int? PlayerPosition { get; set; }

    [JsonProperty("captured_at_utc")]
    public string CapturedAtUtc { get; set; } = string.Empty;

    [JsonProperty("image_format")]
    public string ImageFormat { get; set; } = "png";

    [JsonIgnore]
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

    [JsonProperty("image_bytes_base64")]
    public string ImageBytesBase64
    {
        get => Convert.ToBase64String(ImageBytes);
        set =>
            ImageBytes = string.IsNullOrEmpty(value)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(value);
    }
}
```

- [ ] **Step 4.2: Verify compile**

Run: `dotnet build /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: build succeeds.

- [ ] **Step 4.3: Commit**

```bash
git add Game/Online/Models/BazaarDbScreenshotUploadRequestV3.cs
git commit -m "$(cat <<'EOF'
Add BazaarDbScreenshotUploadRequestV3 DTO

JSON shape matches the server contract in the design doc. ImageBytes is
JsonIgnored and round-trips through ImageBytesBase64 to keep the on-wire
payload base64-encoded while letting C# code work in raw bytes.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Create `BazaarDbScreenshotUploadSnapshot`

**Files:**
- Create: `Game/Screenshots/Upload/BazaarDbScreenshotUploadSnapshot.cs`

- [ ] **Step 5.1: Write the file**

Content of `Game/Screenshots/Upload/BazaarDbScreenshotUploadSnapshot.cs`:

```csharp
#nullable enable
using BazaarPlusPlus.Game.Online.Models;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadSnapshot
{
    public string ScreenshotId { get; init; } = string.Empty;

    public BazaarDbScreenshotUploadRequestV3 Payload { get; init; } =
        new BazaarDbScreenshotUploadRequestV3();
}
```

- [ ] **Step 5.2: Verify compile**

Run: `dotnet build /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: build succeeds.

- [ ] **Step 5.3: Commit**

```bash
git add Game/Screenshots/Upload/BazaarDbScreenshotUploadSnapshot.cs
git commit -m "$(cat <<'EOF'
Add BazaarDbScreenshotUploadSnapshot in-memory shape

Carrier between the store (which builds it) and the service (which sends
it). Mirrors RunBundleUploadSnapshot from the run-bundle uploader.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Create `BazaarDbScreenshotUploadStore` with TDD

This task is the biggest of the mod-side data layer. Build the test project first, then the store, with one micro-task per behavior.

**Files:**
- Create: `tests/BazaarDbScreenshotUploadStore.Tests/BazaarDbScreenshotUploadStore.Tests.csproj`
- Create: `tests/BazaarDbScreenshotUploadStore.Tests/Program.cs`
- Create: `Game/Screenshots/Upload/BazaarDbScreenshotUploadStore.cs`

- [ ] **Step 6.1: Create the test csproj**

Write `tests/BazaarDbScreenshotUploadStore.Tests/BazaarDbScreenshotUploadStore.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreAdditionalProjectSources>
      https://api.nuget.org/v3/index.json;
      https://nuget.bepinex.dev/v3/index.json;
    </RestoreAdditionalProjectSources>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../BazaarPlusPlus.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6.2: Write the failing test harness**

Write `tests/BazaarDbScreenshotUploadStore.Tests/Program.cs`. Tests verify, in order: ctor exists, `EnsureBackfilled` enqueues exactly one pending row per orphaned `run_screenshots` row (capture_source = 'end_of_run_auto'), idempotency of `EnsureBackfilled`, `GetPending` returns oldest-first up to `limit`, `MarkUploaded` flips status to 'uploaded' and clears `last_error`, `MarkTransientFailure` keeps `pending` and increments `attempts`, `MarkPermanentFailure` flips status to 'permanent_failure'.

```csharp
#nullable enable
using System.Reflection;
using Microsoft.Data.Sqlite;

var schemaType = RequireType(
    "BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite.RunLogSqliteSchema"
);
var storeType = RequireType("BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbScreenshotUploadStore");

var ctor = storeType.GetConstructor([typeof(string), typeof(string)]);
Assert(
    ctor != null,
    "BazaarDbScreenshotUploadStore should expose a constructor taking the database path and screenshots directory."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-bazaardb-screenshot-upload-store-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "test.db");
var screenshotsDir = Path.Combine(tempRoot, "screenshots");
Directory.CreateDirectory(screenshotsDir);

try
{
    // Bootstrap the schema by touching the existing RunScreenshotSqliteStore (it inherits SqlitePersistenceStoreBase
    // which runs RunLogSqliteSchema.EnsureInitialized in the ctor).
    var screenshotStoreType = RequireType(
        "BazaarPlusPlus.Game.Screenshots.Persistence.RunScreenshotSqliteStore"
    );
    Activator.CreateInstance(screenshotStoreType, dbPath);

    SeedRunScreenshotRow(dbPath, "shot-A", capturedAtUtc: "2026-04-08T20:30:25.000Z");
    SeedRunScreenshotRow(dbPath, "shot-B", capturedAtUtc: "2026-04-08T20:31:25.000Z");
    SeedRunScreenshotRow(
        dbPath,
        "shot-other-source",
        capturedAtUtc: "2026-04-08T20:32:25.000Z",
        captureSource: "manual"
    );

    var store = ctor!.Invoke([dbPath, screenshotsDir]);
    var ensureBackfilled = storeType.GetMethod(
        "EnsureBackfilled",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(ensureBackfilled != null, "BazaarDbScreenshotUploadStore should expose EnsureBackfilled.");
    ensureBackfilled!.Invoke(store, []);

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            CountRows(connection, "bazaardb_screenshot_uploads") == 2,
            "EnsureBackfilled should insert exactly one pending row per end-of-run screenshot."
        );
        Assert(
            GetString(
                connection,
                "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-A"
            ) == "pending",
            "Backfilled rows should start in 'pending' status."
        );
    }

    // Idempotency: calling EnsureBackfilled twice must not duplicate.
    ensureBackfilled.Invoke(store, []);
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            CountRows(connection, "bazaardb_screenshot_uploads") == 2,
            "EnsureBackfilled should be idempotent (INSERT OR IGNORE)."
        );
    }

    var getPending = storeType.GetMethod(
        "GetPendingScreenshotIds",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(getPending != null, "BazaarDbScreenshotUploadStore should expose GetPendingScreenshotIds.");
    var pending = (System.Collections.Generic.IReadOnlyList<string>)
        getPending!.Invoke(store, [10])!;
    Assert(pending.Count == 2, "GetPendingScreenshotIds should return both backfilled rows.");
    Assert(pending[0] == "shot-A", "Pending ordering should be by captured_at_utc ASC.");
    Assert(pending[1] == "shot-B", "Pending ordering should be by captured_at_utc ASC.");

    var pendingLimited = (System.Collections.Generic.IReadOnlyList<string>)
        getPending.Invoke(store, [1])!;
    Assert(pendingLimited.Count == 1, "GetPendingScreenshotIds should respect the limit argument.");

    // MarkUploaded
    var markUploaded = storeType.GetMethod(
        "MarkUploaded",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(markUploaded != null, "BazaarDbScreenshotUploadStore should expose MarkUploaded.");
    markUploaded!.Invoke(
        store,
        ["shot-A", DateTimeOffset.Parse("2026-04-08T21:00:00.000Z").UtcDateTime]
    );
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetString(
                connection,
                "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-A"
            ) == "uploaded",
            "MarkUploaded should set status to 'uploaded'."
        );
    }

    // MarkTransientFailure
    var markTransient = storeType.GetMethod(
        "MarkTransientFailure",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(
        markTransient != null,
        "BazaarDbScreenshotUploadStore should expose MarkTransientFailure."
    );
    markTransient!.Invoke(
        store,
        ["shot-B", DateTimeOffset.Parse("2026-04-08T21:00:00.000Z").UtcDateTime, "net_timeout"]
    );
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetString(
                connection,
                "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-B"
            ) == "pending",
            "MarkTransientFailure should keep status as 'pending'."
        );
        Assert(
            GetInt64(
                connection,
                "SELECT attempts FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-B"
            ) == 1,
            "MarkTransientFailure should increment attempts."
        );
    }

    // MarkPermanentFailure
    var markPermanent = storeType.GetMethod(
        "MarkPermanentFailure",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(
        markPermanent != null,
        "BazaarDbScreenshotUploadStore should expose MarkPermanentFailure."
    );
    markPermanent!.Invoke(
        store,
        ["shot-B", DateTimeOffset.Parse("2026-04-08T21:00:00.000Z").UtcDateTime, "schema_mismatch"]
    );
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetString(
                connection,
                "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;",
                "shot-B"
            ) == "permanent_failure",
            "MarkPermanentFailure should flip status to 'permanent_failure'."
        );
    }

    // GetPending after marking: only pending rows should be returned.
    var pendingAfter = (System.Collections.Generic.IReadOnlyList<string>)
        getPending.Invoke(store, [10])!;
    Assert(pendingAfter.Count == 0, "After marks, no rows should remain pending.");
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch { }
}

Console.WriteLine("BazaarDbScreenshotUploadStore checks passed.");

static void SeedRunScreenshotRow(
    string dbPath,
    string screenshotId,
    string capturedAtUtc,
    string captureSource = "end_of_run_auto"
)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
        INSERT INTO run_screenshots (
            screenshot_id, run_id, hero_name, battle_id, capture_source, is_primary,
            image_relative_path, captured_at_local, captured_at_utc, day, player_rank,
            player_rating, player_position, victories_at_capture
        ) VALUES (
            $id, NULL, NULL, NULL, $src, 0,
            'test/' || $id || '.png', $ts, $ts, NULL, NULL, NULL, NULL, NULL
        );
        """;
    cmd.Parameters.AddWithValue("$id", screenshotId);
    cmd.Parameters.AddWithValue("$src", captureSource);
    cmd.Parameters.AddWithValue("$ts", capturedAtUtc);
    cmd.ExecuteNonQuery();
}

static long CountRows(SqliteConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
    return (long)(command.ExecuteScalar() ?? 0L);
}

static string GetString(SqliteConnection connection, string sql, string id)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", id);
    return (string)(command.ExecuteScalar() ?? throw new InvalidOperationException(sql));
}

static long GetInt64(SqliteConnection connection, string sql, string id)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", id);
    return (long)(command.ExecuteScalar() ?? throw new InvalidOperationException(sql));
}

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
```

- [ ] **Step 6.3: Run the test to confirm it fails (RED)**

Run: `dotnet run --project tests/BazaarDbScreenshotUploadStore.Tests/`
Expected: fails with "Type not found: BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbScreenshotUploadStore".

- [ ] **Step 6.4: Implement the store**

Write `Game/Screenshots/Upload/BazaarDbScreenshotUploadStore.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Online.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadStore : SqlitePersistenceStoreBase
{
    private const int UploadPayloadSchemaVersion = 1;

    private readonly string _screenshotsDirectoryPath;

    public BazaarDbScreenshotUploadStore(string databasePath, string screenshotsDirectoryPath)
        : base(databasePath)
    {
        if (string.IsNullOrWhiteSpace(screenshotsDirectoryPath))
            throw new ArgumentException(
                "Screenshots directory is required.",
                nameof(screenshotsDirectoryPath)
            );
        _screenshotsDirectoryPath = screenshotsDirectoryPath;
    }

    public void EnsureBackfilled()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            INSERT OR IGNORE INTO {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
                (screenshot_id, status, attempts, last_attempted_at_utc, last_error, uploaded_at_utc)
            SELECT s.screenshot_id, 'pending', 0, NULL, NULL, NULL
            FROM {RunLogSqliteSchema.RunScreenshotsTableName} AS s
            WHERE s.capture_source = 'end_of_run_auto'
              AND NOT EXISTS (
                  SELECT 1
                  FROM {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName} AS u
                  WHERE u.screenshot_id = s.screenshot_id
              );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetPendingScreenshotIds(int limit)
    {
        if (limit <= 0)
            return Array.Empty<string>();

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT u.screenshot_id
            FROM {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName} AS u
            INNER JOIN {RunLogSqliteSchema.RunScreenshotsTableName} AS s
                ON s.screenshot_id = u.screenshot_id
            WHERE u.status = 'pending'
            ORDER BY s.captured_at_utc ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    public bool HasMorePending()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT 1 FROM {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
            WHERE status = 'pending'
            LIMIT 1;
            """;
        return command.ExecuteScalar() != null;
    }

    public BazaarDbScreenshotUploadSnapshot? TryBuildSnapshot(
        string screenshotId,
        string playerAccountId
    )
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT
                screenshot_id,
                run_id,
                hero_name,
                image_relative_path,
                captured_at_utc,
                day,
                player_rank,
                player_rating,
                player_position,
                victories_at_capture
            FROM {RunLogSqliteSchema.RunScreenshotsTableName}
            WHERE screenshot_id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", screenshotId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var imageRelativePath = reader.GetString(reader.GetOrdinal("image_relative_path"));
        var absolutePath = Path.Combine(_screenshotsDirectoryPath, imageRelativePath);
        if (!File.Exists(absolutePath))
            return null;

        var bytes = File.ReadAllBytes(absolutePath);
        if (bytes.Length == 0)
            return null;

        var playerName = TryResolvePlayerName();

        return new BazaarDbScreenshotUploadSnapshot
        {
            ScreenshotId = screenshotId,
            Payload = new BazaarDbScreenshotUploadRequestV3
            {
                SchemaVersion = UploadPayloadSchemaVersion,
                SubmittedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                PlayerAccountId = playerAccountId,
                ScreenshotId = screenshotId,
                RunId = GetNullableString(reader, "run_id"),
                HeroName = GetNullableString(reader, "hero_name"),
                FinalDays = GetNullableInt32(reader, "day"),
                FinalVictories = GetNullableInt32(reader, "victories_at_capture"),
                PlayerName = playerName,
                PlayerRank = GetNullableString(reader, "player_rank"),
                PlayerRating = GetNullableInt32(reader, "player_rating"),
                PlayerPosition = GetNullableInt32(reader, "player_position"),
                CapturedAtUtc = reader.GetString(reader.GetOrdinal("captured_at_utc")),
                ImageFormat = "png",
                ImageBytes = bytes,
            },
        };
    }

    public void MarkUploaded(string screenshotId, DateTime uploadedAtUtc)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
            SET status = 'uploaded',
                uploaded_at_utc = $uploadedAtUtc,
                last_attempted_at_utc = $uploadedAtUtc,
                last_error = NULL
            WHERE screenshot_id = $id;
            """;
        command.Parameters.AddWithValue("$id", screenshotId);
        command.Parameters.AddWithValue("$uploadedAtUtc", uploadedAtUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void MarkTransientFailure(string screenshotId, DateTime attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
            SET attempts = attempts + 1,
                last_attempted_at_utc = $attemptedAtUtc,
                last_error = $error
            WHERE screenshot_id = $id;
            """;
        command.Parameters.AddWithValue("$id", screenshotId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error ?? string.Empty);
        command.ExecuteNonQuery();
    }

    public void MarkPermanentFailure(string screenshotId, DateTime attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
            SET status = 'permanent_failure',
                attempts = attempts + 1,
                last_attempted_at_utc = $attemptedAtUtc,
                last_error = $error
            WHERE screenshot_id = $id;
            """;
        command.Parameters.AddWithValue("$id", screenshotId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static string? TryResolvePlayerName()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileDisplayUsername()
                ?? BppClientCacheBridge.TryGetProfileUsername();
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 6.5: Run the test to confirm it passes (GREEN)**

Run: `dotnet run --project tests/BazaarDbScreenshotUploadStore.Tests/`
Expected: prints `BazaarDbScreenshotUploadStore checks passed.` with exit code 0.

- [ ] **Step 6.6: Commit**

```bash
git add tests/BazaarDbScreenshotUploadStore.Tests/ Game/Screenshots/Upload/BazaarDbScreenshotUploadStore.cs
git commit -m "$(cat <<'EOF'
Add BazaarDbScreenshotUploadStore

Sidecar SQLite store mirroring RunBundleUploadStore. EnsureBackfilled is
the single mechanism that registers screenshots — it picks up both legacy
end-of-run rows and ones written by the current session.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Create `BazaarDbScreenshotClient`

**Files:**
- Create: `Game/Online/BazaarDbScreenshotClient.cs`

- [ ] **Step 7.1: Write the file**

Content of `Game/Online/BazaarDbScreenshotClient.cs`:

```csharp
#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.Online.Models;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Online;

internal sealed class BazaarDbScreenshotClient
{
    private readonly HttpClient _httpClient;
    private readonly V3Routes _routes;

    public BazaarDbScreenshotClient(HttpClient httpClient, V3Routes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<BazaarDbScreenshotUploadResult> UploadScreenshotAsync(
        BazaarDbScreenshotUploadRequestV3 payload,
        CancellationToken cancellationToken
    )
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        var bodyBytes = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(payload, V3Serialization.SerializerSettings)
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, _routes.UploadBazaarDbScreenshot)
        {
            Content = new ByteArrayContent(bodyBytes),
        };
        request.Content.Headers.ContentType = new("application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return BazaarDbScreenshotUploadResult.Success();

        var responseBody = await response.Content.ReadAsStringAsync();
        var formattedError = V3ErrorFormatter.FormatHttpFailure(
            (int)response.StatusCode,
            responseBody
        );
        return IsPermanentClientError(response.StatusCode)
            ? BazaarDbScreenshotUploadResult.PermanentFailure(formattedError)
            : BazaarDbScreenshotUploadResult.TransientFailure(formattedError);
    }

    private static bool IsPermanentClientError(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        // 4xx except 408 (Request Timeout) and 429 (Too Many Requests) — design §10.1 maps both to transient.
        return code >= 400 && code < 500 && code != 408 && code != 429;
    }
}

internal readonly struct BazaarDbScreenshotUploadResult
{
    private BazaarDbScreenshotUploadResult(bool succeeded, bool permanent, string? error)
    {
        Succeeded = succeeded;
        Permanent = permanent;
        Error = error;
    }

    public bool Succeeded { get; }

    public bool Permanent { get; }

    public string? Error { get; }

    public static BazaarDbScreenshotUploadResult Success() => new(true, false, null);

    public static BazaarDbScreenshotUploadResult TransientFailure(string error) =>
        new(false, false, error);

    public static BazaarDbScreenshotUploadResult PermanentFailure(string error) =>
        new(false, true, error);
}
```

- [ ] **Step 7.2: Verify compile**

Run: `dotnet build /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: build succeeds.

- [ ] **Step 7.3: Commit**

```bash
git add Game/Online/BazaarDbScreenshotClient.cs
git commit -m "$(cat <<'EOF'
Add BazaarDbScreenshotClient

Stateless HTTP wrapper. 4xx errors (except 408/429) classify as permanent
so the service can short-circuit retry on schema mismatches and 413s.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Create `BazaarDbScreenshotUploadService` with TDD

**Files:**
- Create: `tests/BazaarDbScreenshotUploadService.Tests/BazaarDbScreenshotUploadService.Tests.csproj`
- Create: `tests/BazaarDbScreenshotUploadService.Tests/Program.cs`
- Create: `Game/Screenshots/Upload/BazaarDbScreenshotUploadService.cs`

- [ ] **Step 8.1: Create the test csproj**

Write `tests/BazaarDbScreenshotUploadService.Tests/BazaarDbScreenshotUploadService.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreAdditionalProjectSources>
      https://api.nuget.org/v3/index.json;
      https://nuget.bepinex.dev/v3/index.json;
    </RestoreAdditionalProjectSources>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../BazaarPlusPlus.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 8.2: Write the failing test program**

Write `tests/BazaarDbScreenshotUploadService.Tests/Program.cs`. This test exercises the service against a real store (the schema is created in-process) and a `FakeHttpMessageHandler` injected via reflection on the service's HttpClient field. We verify: happy path uploads one screenshot and marks it 'uploaded'; transient HTTP 503 keeps the row pending and increments attempts; permanent HTTP 400 flips to permanent_failure; missing image file marks permanent_failure; batch size of 3 is respected.

```csharp
#nullable enable
using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.Data.Sqlite;

var serviceType = RequireType(
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbScreenshotUploadService"
);
var storeType = RequireType("BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbScreenshotUploadStore");
var routesType = RequireType("BazaarPlusPlus.Game.Online.V3Routes");

var ctor = serviceType.GetConstructor(
    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic,
    binder: null,
    [storeType, routesType, typeof(HttpClient), typeof(Func<string?>)],
    modifiers: null
);
Assert(
    ctor != null,
    "BazaarDbScreenshotUploadService should take (store, routes, httpClient, playerAccountIdResolver)."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-bazaardb-screenshot-upload-service-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "test.db");
var screenshotsDir = Path.Combine(tempRoot, "screenshots");
Directory.CreateDirectory(screenshotsDir);

try
{
    var screenshotStoreType = RequireType(
        "BazaarPlusPlus.Game.Screenshots.Persistence.RunScreenshotSqliteStore"
    );
    Activator.CreateInstance(screenshotStoreType, dbPath);
    SeedRunScreenshotWithFile(dbPath, screenshotsDir, "shot-1", "2026-04-08_20-30-25-000_final_run-r1.png");

    var storeCtor = storeType.GetConstructor([typeof(string), typeof(string)])!;
    var store = storeCtor.Invoke([dbPath, screenshotsDir]);
    var ensureBackfilled = storeType.GetMethod("EnsureBackfilled")!;
    ensureBackfilled.Invoke(store, []);

    var routes = routesType
        .GetMethod("TryCreate", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, ["https://example.invalid"]);
    Assert(routes != null, "TryCreate should accept https://example.invalid.");

    // Test 1: happy path
    {
        var handler = new RecordingHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}"),
            };
        });
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(handler.Requests.Count == 1, "Service should POST exactly one request.");
        Assert(
            GetUploadStatus(dbPath, "shot-1") == "uploaded",
            "Happy-path row should be marked uploaded."
        );
        client.Dispose();
    }

    // Test 2: transient HTTP 503 keeps row pending and increments attempts
    SeedRunScreenshotWithFile(dbPath, screenshotsDir, "shot-2", "2026-04-08_20-31-25-000_final_run-r1.png");
    ensureBackfilled.Invoke(store, []);
    {
        var handler = new RecordingHandler(req =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("boom"),
            }
        );
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-2") == "pending",
            "Transient failure should leave row pending."
        );
        Assert(
            GetUploadAttempts(dbPath, "shot-2") == 1,
            "Transient failure should increment attempts."
        );
        client.Dispose();
    }

    // Test 3: permanent HTTP 400 flips to permanent_failure
    {
        var handler = new RecordingHandler(req =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"reason\":\"schema_mismatch\"}"),
            }
        );
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-2") == "permanent_failure",
            "HTTP 400 should be classified as a permanent failure."
        );
        client.Dispose();
    }

    // Test 4: missing image file → permanent_failure
    SeedRunScreenshotMissingFile(dbPath, "shot-3");
    ensureBackfilled.Invoke(store, []);
    {
        var handler = new RecordingHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}"),
            }
        );
        var client = new HttpClient(handler);
        var service = ctor!.Invoke([store, routes, client, new Func<string?>(() => "acct-9")]);
        var uploadMethod = serviceType.GetMethod("UploadPendingAsync")!;
        var task = (Task)uploadMethod.Invoke(service, [CancellationToken.None])!;
        task.GetAwaiter().GetResult();

        Assert(
            GetUploadStatus(dbPath, "shot-3") == "permanent_failure",
            "Missing image file should be a permanent failure."
        );
        Assert(
            handler.Requests.Count == 0,
            "Service should not POST when the image file is missing."
        );
        client.Dispose();
    }
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch { }
}

Console.WriteLine("BazaarDbScreenshotUploadService checks passed.");

static void SeedRunScreenshotWithFile(
    string dbPath,
    string screenshotsDir,
    string id,
    string relativePath
)
{
    var absolutePath = Path.Combine(screenshotsDir, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
    File.WriteAllBytes(absolutePath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
        INSERT INTO run_screenshots (
            screenshot_id, run_id, hero_name, battle_id, capture_source, is_primary,
            image_relative_path, captured_at_local, captured_at_utc, day, player_rank,
            player_rating, player_position, victories_at_capture
        ) VALUES (
            $id, NULL, 'TestHero', NULL, 'end_of_run_auto', 0,
            $path, '2026-04-08T20:30:25.000Z', '2026-04-08T20:30:25.000Z', 14, 'Diamond', 1942, 1, 10
        );
        """;
    cmd.Parameters.AddWithValue("$id", id);
    cmd.Parameters.AddWithValue("$path", relativePath);
    cmd.ExecuteNonQuery();
}

static void SeedRunScreenshotMissingFile(string dbPath, string id)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
        INSERT INTO run_screenshots (
            screenshot_id, run_id, hero_name, battle_id, capture_source, is_primary,
            image_relative_path, captured_at_local, captured_at_utc, day, player_rank,
            player_rating, player_position, victories_at_capture
        ) VALUES (
            $id, NULL, NULL, NULL, 'end_of_run_auto', 0,
            'does/not/exist.png', '2026-04-08T20:30:25.000Z', '2026-04-08T20:30:25.000Z', NULL, NULL, NULL, NULL, NULL
        );
        """;
    cmd.Parameters.AddWithValue("$id", id);
    cmd.ExecuteNonQuery();
}

static string GetUploadStatus(string dbPath, string id)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
        "SELECT status FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;";
    command.Parameters.AddWithValue("$id", id);
    return (string?)command.ExecuteScalar() ?? string.Empty;
}

static long GetUploadAttempts(string dbPath, string id)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
        "SELECT attempts FROM bazaardb_screenshot_uploads WHERE screenshot_id = $id;";
    command.Parameters.AddWithValue("$id", id);
    return (long)(command.ExecuteScalar() ?? 0L);
}

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
```

- [ ] **Step 8.3: Run the test to confirm it fails (RED)**

Run: `dotnet run --project tests/BazaarDbScreenshotUploadService.Tests/`
Expected: "Type not found: BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbScreenshotUploadService".

- [ ] **Step 8.4: Implement the service**

Write `Game/Screenshots/Upload/BazaarDbScreenshotUploadService.cs`:

```csharp
#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Online;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadService
{
    private const int BatchSize = 3;
    private const string AnonymousPlayerAccountId = "anonymous-player";

    private readonly BazaarDbScreenshotUploadStore _store;
    private readonly V3Routes _routes;
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _playerAccountIdResolver;

    public BazaarDbScreenshotUploadService(
        BazaarDbScreenshotUploadStore store,
        V3Routes routes,
        HttpClient httpClient,
        Func<string?> playerAccountIdResolver
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _playerAccountIdResolver =
            playerAccountIdResolver ?? throw new ArgumentNullException(nameof(playerAccountIdResolver));
    }

    public async Task UploadPendingAsync(CancellationToken cancellationToken)
    {
        _store.EnsureBackfilled();

        var pending = _store.GetPendingScreenshotIds(BatchSize);
        if (pending.Count == 0)
        {
            BppLog.Info(
                "BazaarDbScreenshotUploadService",
                "No screenshots are waiting for BazaarDB upload."
            );
            return;
        }

        var playerAccountId =
            (_playerAccountIdResolver()?.Trim() is { Length: > 0 } resolved)
                ? resolved
                : AnonymousPlayerAccountId;

        var client = new BazaarDbScreenshotClient(_httpClient, _routes);
        foreach (var screenshotId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTime.UtcNow;
            try
            {
                var snapshot = _store.TryBuildSnapshot(screenshotId, playerAccountId);
                if (snapshot == null)
                {
                    _store.MarkPermanentFailure(
                        screenshotId,
                        attemptedAtUtc,
                        "build_snapshot_failed"
                    );
                    continue;
                }

                var result = await client.UploadScreenshotAsync(snapshot.Payload, cancellationToken);
                if (result.Succeeded)
                {
                    _store.MarkUploaded(screenshotId, DateTime.UtcNow);
                    continue;
                }

                var error = result.Error ?? "bazaardb_upload_failed";
                if (result.Permanent)
                    _store.MarkPermanentFailure(screenshotId, attemptedAtUtc, error);
                else
                    _store.MarkTransientFailure(screenshotId, attemptedAtUtc, error);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _store.MarkTransientFailure(screenshotId, attemptedAtUtc, ex.Message);
            }
        }
    }
}
```

- [ ] **Step 8.5: Run the test to confirm it passes (GREEN)**

Run: `dotnet run --project tests/BazaarDbScreenshotUploadService.Tests/`
Expected: prints `BazaarDbScreenshotUploadService checks passed.` with exit code 0.

- [ ] **Step 8.6: Commit**

```bash
git add tests/BazaarDbScreenshotUploadService.Tests/ Game/Screenshots/Upload/BazaarDbScreenshotUploadService.cs
git commit -m "$(cat <<'EOF'
Add BazaarDbScreenshotUploadService

Cycle orchestrator mirroring RunBundleUploadService. Backfill runs at the
top of every cycle, picking up new screenshots without an event hook.
Permanent vs transient classification is driven by the client.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Create `BazaarDbScreenshotUploadController` MonoBehaviour

**Files:**
- Create: `Game/Screenshots/Upload/BazaarDbScreenshotUploadController.cs`

- [ ] **Step 9.1: Write the file**

Content of `Game/Screenshots/Upload/BazaarDbScreenshotUploadController.cs`:

```csharp
#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Online;
using BazaarPlusPlus.Game.Upload;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadController : MonoBehaviour
{
    private static BazaarDbScreenshotUploadController? _current;

    private IBppServices? _services;
    private BazaarDbScreenshotUploadService? _uploadService;
    private HttpClient? _httpClient;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private IDisposable? _runLifecycleSubscription;
    private readonly StartupUploadAttemptRunner _startupRunner = new(
        "BazaarDbScreenshotUploadController",
        "Skipping BazaarDB screenshot upload because a live run is active.",
        "Starting BazaarDB screenshot upload attempt.",
        "BazaarDB screenshot upload failed"
    );

    private void Awake()
    {
        _current = this;
    }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeCore();
    }

    private void InitializeCore()
    {
        try
        {
            var services = _services!;
            var databasePath = services.Paths.RunLogDatabasePath;
            var screenshotsDirectoryPath = services.Paths.ScreenshotsDirectoryPath;

            if (
                string.IsNullOrWhiteSpace(databasePath)
                || string.IsNullOrWhiteSpace(screenshotsDirectoryPath)
            )
            {
                BppLog.Warn(
                    "BazaarDbScreenshotUploadController",
                    "BazaarDB screenshot upload is enabled but database or screenshots paths are invalid."
                );
                return;
            }

            var routes = V3Routes.TryCreate(V3UploadDefaults.ApiBaseUrl);
            if (routes == null)
                return;

            var store = new BazaarDbScreenshotUploadStore(databasePath, screenshotsDirectoryPath);
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(
                    Math.Max(10, V3UploadDefaults.RequestTimeoutSeconds)
                ),
            };
            _uploadService = new BazaarDbScreenshotUploadService(
                store,
                routes,
                _httpClient,
                BppClientCacheBridge.TryGetProfileAccountId
            );
            _shutdown = new CancellationTokenSource();

            var startupDelaySeconds = Math.Max(5, V3UploadDefaults.StartupDelaySeconds);
            var retryIntervalSeconds = Math.Max(1, V3UploadDefaults.IntervalSeconds);
            _startupGate = new StartupUploadAttemptGate(
                Time.unscaledTime + startupDelaySeconds,
                retryIntervalSeconds
            );

            _runLifecycleSubscription = services.EventBus.Subscribe<RunLifecycleChanged>(
                OnRunLifecycleChanged
            );

            BppLog.Info(
                "BazaarDbScreenshotUploadController",
                $"BazaarDB screenshot uploader armed. enabled={IsEnabled()}, startup_delay={startupDelaySeconds}s, retry_interval={retryIntervalSeconds}s."
            );
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "BazaarDbScreenshotUploadController",
                $"Failed to initialize BazaarDB screenshot upload service: {ex}"
            );
        }
    }

    private void Update()
    {
        if (
            _uploadService == null
            || _shutdown == null
            || _startupGate == null
            || _services == null
        )
            return;

        if (!IsEnabled())
            return;

        _startupRunner.Tick(
            _startupGate,
            Time.unscaledTime,
            _services!.RunContext.IsInGameRun,
            _uploadService.UploadPendingAsync,
            _shutdown.Token
        );
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;

        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;

        if (_shutdown != null)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            _shutdown = null;
        }

        _httpClient?.Dispose();
        _httpClient = null;
        _uploadService = null;
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        if (change.IsInGameRun)
            return;

        _startupGate?.ArmImmediateAttempt(Time.unscaledTime);
    }

    private bool IsEnabled()
    {
        return Plugin.Instance?.Services?.Config?.BazaarDbUploadEnabled?.Value ?? false;
    }

    public static void OnEnabledChanged(bool enabled)
    {
        if (!enabled)
            return;

        _current?._startupGate?.ArmImmediateAttempt(Time.unscaledTime);
        BppLog.Info(
            "BazaarDbScreenshotUploadController",
            "BazaarDB screenshot upload toggle armed an immediate attempt."
        );
    }
}
```

- [ ] **Step 9.2: Check whether `Plugin.Instance` exists**

Run: `grep -n "Plugin.Instance" /Users/yxinyu/codes/bpp/bazaarplusplus-mod/Plugin.cs /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BppComposition.cs 2>/dev/null | head -20`
Expected: may show nothing (Plugin currently has no public `Instance` accessor) OR show existing usage.

If `Plugin.Instance` does not exist, change `IsEnabled()` to look up the config via the `_services` field instead:

```csharp
    private bool IsEnabled()
    {
        return _services?.Config?.BazaarDbUploadEnabled?.Value ?? false;
    }
```

And reach the controller's `_current` from the settings catalog via `BazaarDbScreenshotUploadController.OnEnabledChanged` (already in place).

- [ ] **Step 9.3: Verify compile**

Run: `dotnet build /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: build succeeds.

- [ ] **Step 9.4: Commit**

```bash
git add Game/Screenshots/Upload/BazaarDbScreenshotUploadController.cs
git commit -m "$(cat <<'EOF'
Add BazaarDbScreenshotUploadController MonoBehaviour

Mirrors RunUploadController: StartupUploadAttemptRunner + RunLifecycle
event subscription. IsEnabled() short-circuits at the top of Update so a
disabled toggle issues zero database reads and zero network calls.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: Wire the controller into `Plugin.cs`

**Files:**
- Modify: [`Plugin.cs`](../../../Plugin.cs)

- [ ] **Step 10.1: Add `using` directive**

Replace the `using BazaarPlusPlus.Game.Screenshots;` line (line 19) with:

```csharp
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Screenshots.Upload;
```

- [ ] **Step 10.2: Add controller attachment in `AttachRuntimeComponents`**

In `Plugin.cs` `AttachRuntimeComponents` (lines 147–183), add the controller after `screenshot.Initialize(services);` on line 173, before the `AddConfiguredTooltipModifierRefreshController` call:

```csharp
        var bazaarDbScreenshotUpload =
            gameObject.AddComponent<BazaarDbScreenshotUploadController>();
        bazaarDbScreenshotUpload.Initialize(services);
```

- [ ] **Step 10.3: Add teardown in `DetachRuntimeComponents`**

In `DetachRuntimeComponents` (lines 263–279), add immediately after the `DestroyComponentIfPresent<EndOfRunScreenshotController>();` line:

```csharp
        DestroyComponentIfPresent<BazaarDbScreenshotUploadController>();
```

- [ ] **Step 10.4: Verify compile**

Run: `dotnet build /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: build succeeds.

- [ ] **Step 10.5: Commit**

```bash
git add Plugin.cs
git commit -m "$(cat <<'EOF'
Attach BazaarDbScreenshotUploadController in Plugin

Parallel to RunUploadController and EndOfRunScreenshotController.
Initialize/teardown follow the existing pattern.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Add the label resolver + register settings dock entry

**Files:**
- Create: `Game/Screenshots/Upload/BazaarDbScreenshotUploadSettingsMenuLabel.cs`
- Modify: [`Game/Settings/BppSettingsDockCatalog.cs`](../../../Game/Settings/BppSettingsDockCatalog.cs)

- [ ] **Step 11.1: Write the label file**

Content of `Game/Screenshots/Upload/BazaarDbScreenshotUploadSettingsMenuLabel.cs`. The string set mirrors [`NameOverride.SettingsMenuLabel.cs`](../../../Game/NameOverride/NameOverride.SettingsMenuLabel.cs) — same six positional arguments to `LocalizedTextSet`:

```csharp
#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal static class BazaarDbScreenshotUploadSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Upload screenshots to BazaarDB",
        "上传截图到 BazaarDB",
        "Screenshots zu BazaarDB hochladen",
        "Subir capturas a BazaarDB",
        "스크린샷을 BazaarDB에 업로드",
        "Carica gli screenshot su BazaarDB"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
```

- [ ] **Step 11.2: Add `using` to the dock catalog**

In [`Game/Settings/BppSettingsDockCatalog.cs`](../../../Game/Settings/BppSettingsDockCatalog.cs) add after line 8:

```csharp
using BazaarPlusPlus.Game.Screenshots.Upload;
```

- [ ] **Step 11.3: Add the catalog definition entry**

In `BppSettingsDockCatalog.Definitions` (lines 28–80), append a new entry after the `CombatStatusBar` entry (line 71), before `ChineseLocaleMode`:

```csharp
        new(
            "BazaarDbUpload",
            BazaarDbScreenshotUploadSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                ReadBazaarDbUploadEnabled,
                WriteBazaarDbUploadEnabled,
                BazaarDbScreenshotUploadController.OnEnabledChanged
            )
        ),
```

- [ ] **Step 11.4: Add the read/write helpers**

Append these two methods at the bottom of the class (after `ResolveLegendaryPositionDisplayStatus`):

```csharp
    private static bool ReadBazaarDbUploadEnabled()
    {
        return Config.BazaarDbUploadEnabled?.Value ?? false;
    }

    private static void WriteBazaarDbUploadEnabled(bool enabled)
    {
        var config = Config.BazaarDbUploadEnabled;
        if (config != null)
            config.Value = enabled;
    }
```

- [ ] **Step 11.5: Verify compile**

Run: `dotnet build /Users/yxinyu/codes/bpp/bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: build succeeds.

- [ ] **Step 11.6: Commit**

```bash
git add Game/Screenshots/Upload/BazaarDbScreenshotUploadSettingsMenuLabel.cs Game/Settings/BppSettingsDockCatalog.cs
git commit -m "$(cat <<'EOF'
Register BazaarDB upload toggle in settings dock

OnChanged routes flips to OnEnabledChanged which arms an immediate
upload attempt — flipping the toggle on doesn't wait 180s for the next
tick.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 3 — Server-side (`ModCFServerV3`)

### Task 12: Add D1 migration `0012_create_bazaardb_screenshots.sql`

**Files:**
- Create: `ModCFServerV3/migrations/0012_create_bazaardb_screenshots.sql`

- [ ] **Step 12.1: Write the migration**

Content of `ModCFServerV3/migrations/0012_create_bazaardb_screenshots.sql`:

```sql
-- Migration 0012: BazaarDB screenshot store.
-- Mod posts screenshots here via POST /bazaardb-screenshots.
-- BazaarDB pulls daily via GET /bazaardb/manifest?date=YYYY-MM-DD.

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
  captured_date_utc    TEXT NOT NULL,
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

- [ ] **Step 12.2: Verify against existing migrations**

Run: `ls /Users/yxinyu/codes/bpp/bazaarplusplus-mod/ModCFServerV3/migrations/`
Expected: lists `0011_create_seen_player_accounts.sql` and the new `0012_create_bazaardb_screenshots.sql`.

- [ ] **Step 12.3: Commit**

```bash
git add ModCFServerV3/migrations/0012_create_bazaardb_screenshots.sql
git commit -m "$(cat <<'EOF'
Add bazaardb_screenshots D1 migration

Primary-key UPSERT is the only mutation on the ingest path. Index covers
the manifest query: (captured_date_utc, uploaded_at_utc).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 13: Add R2 binding + bearer-token secret + env types

**Files:**
- Modify: [`ModCFServerV3/wrangler.toml`](../../../ModCFServerV3/wrangler.toml)
- Modify: [`ModCFServerV3/src/env.ts`](../../../ModCFServerV3/src/env.ts)
- Modify: [`ModCFServerV3/test/env.d.ts`](../../../ModCFServerV3/test/env.d.ts)

- [ ] **Step 13.1: Add R2 binding to wrangler**

Append to [`ModCFServerV3/wrangler.toml`](../../../ModCFServerV3/wrangler.toml) (after the existing `[[r2_buckets]]` block on lines 28–30):

```toml

[[r2_buckets]]
binding = "BAZAARDB_BUCKET"
bucket_name = "bazaarplusplus-bazaardb-assets"
```

Do NOT add the secret to `wrangler.toml` — it is set out-of-band via `wrangler secret put BAZAARDB_PULL_TOKEN` and never committed.

- [ ] **Step 13.2: Extend `src/env.ts`**

Replace the entire content of [`ModCFServerV3/src/env.ts`](../../../ModCFServerV3/src/env.ts):

```typescript
export interface Env {
  DB: D1Database;
  RUN_BUNDLE_BUCKET: R2Bucket;
  BAZAARDB_BUCKET: R2Bucket;
  REPLAY_DOWNLOAD_SECRET: string;
  BAZAARDB_PULL_TOKEN: string;
  GHOST_QUERY_LOOKBACK_DAYS: string;
  RUN_BUNDLE_RETENTION_DAYS: string;
}
```

- [ ] **Step 13.3: Mirror in `test/env.d.ts`**

In [`ModCFServerV3/test/env.d.ts`](../../../ModCFServerV3/test/env.d.ts), add the two new fields:

```typescript
/// <reference types="@cloudflare/vitest-pool-workers/types" />

declare namespace Cloudflare {
  interface Env {
    DB: D1Database;
    RUN_BUNDLE_BUCKET: R2Bucket;
    BAZAARDB_BUCKET: R2Bucket;
    REPLAY_DOWNLOAD_SECRET: string;
    BAZAARDB_PULL_TOKEN: string;
    GHOST_QUERY_LOOKBACK_DAYS: string;
    RUN_BUNDLE_RETENTION_DAYS: string;
    TEST_MIGRATIONS: import("@cloudflare/vitest-pool-workers").D1Migration[];
  }
}

declare module "*.sql?raw" {
  const content: string;
  export default content;
}
```

- [ ] **Step 13.4: Configure vitest pool to provide a test R2 binding and token**

Look at `ModCFServerV3/vitest.config.ts` and see what miniflare bindings are configured. Run: `cat /Users/yxinyu/codes/bpp/bazaarplusplus-mod/ModCFServerV3/vitest.config.ts`.

If miniflare bindings are configured inline in `vitest.config.ts`, add the new R2 binding `BAZAARDB_BUCKET` and a `BAZAARDB_PULL_TOKEN: "test-pull-token"` next to the existing `RUN_BUNDLE_BUCKET` configuration. If bindings come from wrangler.toml automatically, the test binding for the R2 bucket is created automatically — confirm by reading the comments in the file.

For example, if you see:
```typescript
miniflare: {
  r2Buckets: ["RUN_BUNDLE_BUCKET"],
  bindings: { REPLAY_DOWNLOAD_SECRET: "test-secret", ... },
}
```
Change it to:
```typescript
miniflare: {
  r2Buckets: ["RUN_BUNDLE_BUCKET", "BAZAARDB_BUCKET"],
  bindings: {
    REPLAY_DOWNLOAD_SECRET: "test-secret",
    BAZAARDB_PULL_TOKEN: "test-pull-token",
    ...
  },
}
```

- [ ] **Step 13.5: Verify type-check**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-mod/ModCFServerV3 && npm run check`
Expected: type-check passes.

- [ ] **Step 13.6: Commit**

```bash
git add ModCFServerV3/wrangler.toml ModCFServerV3/src/env.ts ModCFServerV3/test/env.d.ts ModCFServerV3/vitest.config.ts
git commit -m "$(cat <<'EOF'
Wire BAZAARDB_BUCKET R2 binding and BAZAARDB_PULL_TOKEN secret

Secret is set out-of-band via wrangler secret put — not committed.
Vitest miniflare gets a constant 'test-pull-token' so manifest/image
tests can exercise the bearer-auth path.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 14: Implement `POST /bazaardb-screenshots` handler with TDD

**Files:**
- Create: `ModCFServerV3/src/features/v3/uploadBazaarDbScreenshot.ts`
- Create: `ModCFServerV3/test/v3.bazaardbScreenshots.test.ts`

- [ ] **Step 14.1: Write the failing test for the ingest happy path**

Write `ModCFServerV3/test/v3.bazaardbScreenshots.test.ts`:

```typescript
import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import { countRows, selectFirst } from "./helpers/seed";

// PNG magic bytes followed by a tiny payload.
const PNG_BYTES = new Uint8Array([
  0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d,
]);

const PNG_BASE64 = Buffer.from(PNG_BYTES).toString("base64");

function buildIngestRequest(body: unknown): Request {
  return new Request("https://example.com/bazaardb-screenshots", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });
}

async function listScreenshotKeys(): Promise<string[]> {
  const listing = await env.BAZAARDB_BUCKET.list();
  return listing.objects.map((object) => object.key).sort();
}

async function resetBazaarDbState(): Promise<void> {
  await env.DB.prepare("DELETE FROM bazaardb_screenshots").run();
  let cursor: string | undefined;
  do {
    const page = await env.BAZAARDB_BUCKET.list({ cursor });
    if (page.objects.length > 0) {
      await env.BAZAARDB_BUCKET.delete(page.objects.map((o) => o.key));
    }
    cursor = page.truncated ? page.cursor : undefined;
  } while (cursor != null);
}

beforeEach(async () => {
  await resetBazaarDbState();
});

test("ingest stores R2 object and D1 row", async () => {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-1",
    run_id: "run-77",
    hero_name: "Mak",
    final_days: 14,
    final_victories: 10,
    player_name: "Xinyu",
    player_rank: "Diamond",
    player_rating: 1942,
    player_position: 1,
    captured_at_utc: "2026-05-23T20:30:05+00:00",
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(200);
  const body = (await response.json()) as { status: string; screenshot_id: string };
  expect(body.status).toBe("ok");
  expect(body.screenshot_id).toBe("snap-1");

  expect(await listScreenshotKeys()).toEqual([
    "bazaardb/screenshots/2026-05-23/snap-1.png",
  ]);
  expect(await countRows(env.DB, "bazaardb_screenshots")).toBe(1);

  const row = await selectFirst<{
    screenshot_id: string;
    captured_date_utc: string;
    r2_key: string;
    image_bytes: number;
  }>(
    env.DB,
    "SELECT screenshot_id, captured_date_utc, r2_key, image_bytes FROM bazaardb_screenshots WHERE screenshot_id = ?",
    "snap-1",
  );
  expect(row?.captured_date_utc).toBe("2026-05-23");
  expect(row?.r2_key).toBe("bazaardb/screenshots/2026-05-23/snap-1.png");
  expect(row?.image_bytes).toBe(PNG_BYTES.length);
});

test("ingest accepts idempotent re-POST of the same screenshot id", async () => {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-dup",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const first = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(first.status).toBe(200);
  const second = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(second.status).toBe(200);

  expect(await countRows(env.DB, "bazaardb_screenshots")).toBe(1);
  expect((await listScreenshotKeys()).length).toBe(1);
});

test("ingest rejects unsupported schema_version with 400", async () => {
  const payload = {
    schema_version: 99,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-bad-version",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(400);
});

test("ingest rejects when image bytes are not a PNG", async () => {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-not-png",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: Buffer.from(new Uint8Array([0, 1, 2, 3])).toString("base64"),
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(400);
});

test("ingest rejects future captured_at_utc with 400", async () => {
  const futureIso = new Date(Date.now() + 86_400_000 * 365).toISOString();
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: "snap-future",
    captured_at_utc: futureIso,
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(400);
});

test("ingest requires player_account_id", async () => {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    screenshot_id: "snap-no-account",
    captured_at_utc: "2026-05-23T20:30:05Z",
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };

  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(400);
});
```

- [ ] **Step 14.2: Run the test to confirm it fails**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-mod/ModCFServerV3 && npx vitest run test/v3.bazaardbScreenshots.test.ts`
Expected: all tests fail with "not_found" (handler + route don't exist yet).

- [ ] **Step 14.3: Implement the handler**

Write `ModCFServerV3/src/features/v3/uploadBazaarDbScreenshot.ts`:

```typescript
import { base64ToBytes } from "../../crypto/base64";
import { sha256Base64 } from "../../crypto/hash";
import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import { optionalFiniteNumber, optionalTrimmedString } from "../../http/request";
import { logInfo, logWarn } from "../../observability";

type BazaarDbScreenshotRequest = {
  schema_version?: unknown;
  submitted_at_utc?: unknown;
  player_account_id?: unknown;
  screenshot_id?: unknown;
  run_id?: unknown;
  hero_name?: unknown;
  final_days?: unknown;
  final_victories?: unknown;
  player_name?: unknown;
  player_rank?: unknown;
  player_rating?: unknown;
  player_position?: unknown;
  captured_at_utc?: unknown;
  image_format?: unknown;
  image_bytes_base64?: unknown;
};

const SupportedSchemaVersion = 1;
const PngMagic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

const ScreenshotIdPattern = /^[A-Za-z0-9._-]{1,128}$/;

function hasPngMagic(bytes: Uint8Array): boolean {
  if (bytes.length < PngMagic.length) {
    return false;
  }
  for (let index = 0; index < PngMagic.length; index += 1) {
    if (bytes[index] !== PngMagic[index]) {
      return false;
    }
  }
  return true;
}

function parseCapturedDateUtc(value: string): string | null {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }
  if (parsed.getTime() > Date.now() + 60_000) {
    return null;
  }
  const yyyy = parsed.getUTCFullYear().toString().padStart(4, "0");
  const mm = (parsed.getUTCMonth() + 1).toString().padStart(2, "0");
  const dd = parsed.getUTCDate().toString().padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

function rejected(reason: string): Response {
  return json({ status: "rejected", reason }, { status: 400 });
}

export async function handleUploadBazaarDbScreenshot(
  request: Request,
  env: Env,
): Promise<Response> {
  const body = (await readJson(request)) as BazaarDbScreenshotRequest;

  const schemaVersion = optionalFiniteNumber(body.schema_version);
  if (schemaVersion !== SupportedSchemaVersion) {
    return rejected("unsupported_schema_version");
  }

  const submittedAtUtc = optionalTrimmedString(body.submitted_at_utc);
  const playerAccountId = optionalTrimmedString(body.player_account_id);
  const screenshotId = optionalTrimmedString(body.screenshot_id);
  const capturedAtUtc = optionalTrimmedString(body.captured_at_utc);
  const imageFormat = optionalTrimmedString(body.image_format);
  const imageBytesBase64 =
    typeof body.image_bytes_base64 === "string" ? body.image_bytes_base64 : null;

  if (
    submittedAtUtc == null ||
    playerAccountId == null ||
    screenshotId == null ||
    capturedAtUtc == null ||
    imageFormat == null ||
    imageBytesBase64 == null
  ) {
    return rejected("missing_required_field");
  }

  if (!ScreenshotIdPattern.test(screenshotId)) {
    return rejected("invalid_screenshot_id");
  }

  if (imageFormat !== "png") {
    return rejected("unsupported_image_format");
  }

  const capturedDateUtc = parseCapturedDateUtc(capturedAtUtc);
  if (capturedDateUtc == null) {
    return rejected("invalid_captured_at_utc");
  }

  let imageBytes: Uint8Array;
  try {
    imageBytes = base64ToBytes(imageBytesBase64);
  } catch {
    return rejected("invalid_image_bytes_base64");
  }

  if (imageBytes.length === 0 || !hasPngMagic(imageBytes)) {
    return rejected("image_bytes_not_png");
  }

  const r2Key = `bazaardb/screenshots/${capturedDateUtc}/${screenshotId}.png`;

  await env.BAZAARDB_BUCKET.put(r2Key, imageBytes, {
    httpMetadata: { contentType: "image/png" },
  });

  const imageSha256 = await sha256Base64(imageBytes);
  const uploadedAtUtc = new Date().toISOString();

  try {
    await env.DB.prepare(
      `
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
          r2_key          = excluded.r2_key
      `,
    )
      .bind(
        screenshotId,
        playerAccountId,
        optionalTrimmedString(body.run_id),
        optionalTrimmedString(body.hero_name),
        optionalFiniteNumber(body.final_days),
        optionalFiniteNumber(body.final_victories),
        optionalTrimmedString(body.player_name),
        optionalTrimmedString(body.player_rank),
        optionalFiniteNumber(body.player_rating),
        optionalFiniteNumber(body.player_position),
        capturedAtUtc,
        capturedDateUtc,
        imageFormat,
        imageSha256,
        imageBytes.length,
        r2Key,
        uploadedAtUtc,
        SupportedSchemaVersion,
      )
      .run();
  } catch (error) {
    logWarn("upload_bazaardb_screenshot.db_upsert_failed", {
      screenshot_id: screenshotId,
      error: String(error),
    });
    return json(
      { status: "error", reason: "db_upsert_failed" },
      { status: 500 },
    );
  }

  logInfo("upload_bazaardb_screenshot.accepted", {
    screenshot_id: screenshotId,
    captured_date_utc: capturedDateUtc,
    image_bytes: imageBytes.length,
  });

  return json({
    status: "ok",
    screenshot_id: screenshotId,
    uploaded_at_utc: uploadedAtUtc,
  });
}
```

(Route registration in `index.ts` happens in Task 17 — tests will still fail until then.)

- [ ] **Step 14.4: Defer test verification to Task 17**

(Tests will still fail until `index.ts` registers the route. We complete the implementation now and run all three handlers + tests together at the end of Task 17.)

- [ ] **Step 14.5: Commit**

```bash
git add ModCFServerV3/src/features/v3/uploadBazaarDbScreenshot.ts ModCFServerV3/test/v3.bazaardbScreenshots.test.ts
git commit -m "$(cat <<'EOF'
Add POST /bazaardb-screenshots ingest handler

Single-row UPSERT on primary key — passes the scan-shape rule in CLAUDE.md.
Validates schema version, PNG magic bytes, and captured_at_utc not in the
future. R2 put precedes the D1 upsert; orphaned objects acceptable per
design §7.2.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 15: Implement `GET /bazaardb/manifest` handler with TDD

**Files:**
- Create: `ModCFServerV3/src/features/v3/getBazaarDbManifest.ts`
- Modify: `ModCFServerV3/test/v3.bazaardbScreenshots.test.ts`

- [ ] **Step 15.1: Append manifest tests**

Append these tests to the existing `ModCFServerV3/test/v3.bazaardbScreenshots.test.ts`:

```typescript
function buildManifestRequest(date: string, authorization?: string): Request {
  const headers = new Headers();
  if (authorization != null) {
    headers.set("Authorization", authorization);
  }
  return new Request(`https://example.com/bazaardb/manifest?date=${date}`, {
    method: "GET",
    headers,
  });
}

async function ingestOne(
  screenshotId: string,
  capturedAtUtc: string,
): Promise<void> {
  const payload = {
    schema_version: 1,
    submitted_at_utc: "2026-05-24T12:34:56.789Z",
    player_account_id: "acct-9",
    screenshot_id: screenshotId,
    captured_at_utc: capturedAtUtc,
    image_format: "png",
    image_bytes_base64: PNG_BASE64,
  };
  const response = await worker.fetch(buildIngestRequest(payload), env as never);
  expect(response.status).toBe(200);
}

test("manifest returns 401 with no token", async () => {
  const response = await worker.fetch(
    buildManifestRequest("2026-05-23"),
    env as never,
  );
  expect(response.status).toBe(401);
});

test("manifest returns 401 with wrong token", async () => {
  const response = await worker.fetch(
    buildManifestRequest("2026-05-23", "Bearer wrong"),
    env as never,
  );
  expect(response.status).toBe(401);
});

test("manifest returns 400 for invalid date", async () => {
  const response = await worker.fetch(
    buildManifestRequest("not-a-date", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(400);
});

test("manifest returns 200 with empty items when no screenshots for the date", async () => {
  const response = await worker.fetch(
    buildManifestRequest("2026-05-23", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(200);
  const body = (await response.json()) as { items: unknown[]; date: string };
  expect(body.date).toBe("2026-05-23");
  expect(body.items).toEqual([]);
});

test("manifest returns ascending-by-uploaded items for the requested date", async () => {
  await ingestOne("snap-day-a-1", "2026-05-23T01:00:00Z");
  await ingestOne("snap-day-a-2", "2026-05-23T02:00:00Z");
  await ingestOne("snap-day-b-1", "2026-05-24T01:00:00Z");

  const response = await worker.fetch(
    buildManifestRequest("2026-05-23", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(200);
  const body = (await response.json()) as {
    items: { screenshot_id: string; image_url: string }[];
  };
  expect(body.items.length).toBe(2);
  expect(body.items.map((i) => i.screenshot_id)).toEqual([
    "snap-day-a-1",
    "snap-day-a-2",
  ]);
  expect(body.items[0]?.image_url).toMatch(/\/bazaardb\/image\/snap-day-a-1$/);
});
```

- [ ] **Step 15.2: Implement the manifest handler**

Write `ModCFServerV3/src/features/v3/getBazaarDbManifest.ts`:

```typescript
import type { Env } from "../../env";
import { json } from "../../http/json";

const DatePattern = /^\d{4}-\d{2}-\d{2}$/;

function constantTimeEquals(a: string, b: string): boolean {
  if (a.length !== b.length) {
    return false;
  }
  let result = 0;
  for (let index = 0; index < a.length; index += 1) {
    result |= a.charCodeAt(index) ^ b.charCodeAt(index);
  }
  return result === 0;
}

function authorizeBearer(request: Request, env: Env): boolean {
  const header = request.headers.get("Authorization");
  if (header == null) {
    return false;
  }
  const prefix = "Bearer ";
  if (!header.startsWith(prefix)) {
    return false;
  }
  const presented = header.slice(prefix.length).trim();
  const expected = (env.BAZAARDB_PULL_TOKEN ?? "").trim();
  if (expected.length === 0) {
    return false;
  }
  return constantTimeEquals(presented, expected);
}

function validateDate(date: string | null): string | null {
  if (date == null || !DatePattern.test(date)) {
    return null;
  }
  const parsed = new Date(`${date}T00:00:00Z`);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }
  if (parsed.getTime() > Date.now() + 60_000) {
    return null;
  }
  return date;
}

type BazaarDbScreenshotRow = {
  screenshot_id: string;
  player_account_id: string;
  run_id: string | null;
  hero_name: string | null;
  final_days: number | null;
  final_victories: number | null;
  player_name: string | null;
  player_rank: string | null;
  player_rating: number | null;
  player_position: number | null;
  captured_at_utc: string;
  image_format: string;
  image_sha256: string;
  image_bytes: number;
  uploaded_at_utc: string;
};

function buildImageUrl(request: Request, screenshotId: string): string {
  const url = new URL(request.url);
  return `${url.protocol}//${url.host}/bazaardb/image/${encodeURIComponent(screenshotId)}`;
}

export async function handleGetBazaarDbManifest(
  request: Request,
  env: Env,
): Promise<Response> {
  if (!authorizeBearer(request, env)) {
    return new Response(null, { status: 401 });
  }

  const url = new URL(request.url);
  const validatedDate = validateDate(url.searchParams.get("date"));
  if (validatedDate == null) {
    return json({ error: "invalid_date" }, { status: 400 });
  }

  const result = await env.DB.prepare(
    `
      SELECT screenshot_id, player_account_id, run_id, hero_name, final_days,
             final_victories, player_name, player_rank, player_rating, player_position,
             captured_at_utc, image_format, image_sha256, image_bytes, uploaded_at_utc
      FROM bazaardb_screenshots
      WHERE captured_date_utc = ?
      ORDER BY uploaded_at_utc ASC
    `,
  )
    .bind(validatedDate)
    .all<BazaarDbScreenshotRow>();

  const items = result.results.map((row) => ({
    screenshot_id: row.screenshot_id,
    run_id: row.run_id,
    hero_name: row.hero_name,
    final_days: row.final_days,
    final_victories: row.final_victories,
    player_name: row.player_name,
    player_account_id: row.player_account_id,
    player_rank: row.player_rank,
    player_rating: row.player_rating,
    player_position: row.player_position,
    captured_at_utc: row.captured_at_utc,
    image_url: buildImageUrl(request, row.screenshot_id),
    image_format: row.image_format,
    image_sha256: row.image_sha256,
    image_bytes: row.image_bytes,
  }));

  return json({
    date: validatedDate,
    schema_version: 1,
    generated_at_utc: new Date().toISOString(),
    items,
  });
}
```

- [ ] **Step 15.3: Defer test verification to Task 17**

(Routes are registered together in Task 17.)

- [ ] **Step 15.4: Commit**

```bash
git add ModCFServerV3/src/features/v3/getBazaarDbManifest.ts ModCFServerV3/test/v3.bazaardbScreenshots.test.ts
git commit -m "$(cat <<'EOF'
Add GET /bazaardb/manifest handler

Bearer auth uses constant-time compare. Index-backed query
(captured_date_utc, uploaded_at_utc) ASC. Empty days return 200 with
[] so the BazaarDB puller can hit any date unconditionally.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 16: Implement `GET /bazaardb/image/{id}` handler

**Files:**
- Create: `ModCFServerV3/src/features/v3/getBazaarDbImage.ts`
- Modify: `ModCFServerV3/test/v3.bazaardbScreenshots.test.ts`

- [ ] **Step 16.1: Append image-proxy tests**

Append to `ModCFServerV3/test/v3.bazaardbScreenshots.test.ts`:

```typescript
function buildImageRequest(id: string, authorization?: string): Request {
  const headers = new Headers();
  if (authorization != null) {
    headers.set("Authorization", authorization);
  }
  return new Request(`https://example.com/bazaardb/image/${id}`, {
    method: "GET",
    headers,
  });
}

test("image proxy returns 401 without token", async () => {
  const response = await worker.fetch(
    buildImageRequest("snap-1"),
    env as never,
  );
  expect(response.status).toBe(401);
});

test("image proxy returns 404 for unknown screenshot", async () => {
  const response = await worker.fetch(
    buildImageRequest("does-not-exist", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(404);
});

test("image proxy streams the PNG bytes", async () => {
  await ingestOne("snap-image-stream", "2026-05-23T01:00:00Z");

  const response = await worker.fetch(
    buildImageRequest("snap-image-stream", "Bearer test-pull-token"),
    env as never,
  );
  expect(response.status).toBe(200);
  expect(response.headers.get("content-type")).toBe("image/png");
  const bytes = new Uint8Array(await response.arrayBuffer());
  expect(bytes.length).toBe(PNG_BYTES.length);
  expect(bytes.slice(0, 8)).toEqual(
    new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  );
});
```

- [ ] **Step 16.2: Implement the handler**

Write `ModCFServerV3/src/features/v3/getBazaarDbImage.ts`:

```typescript
import type { Env } from "../../env";
import { json } from "../../http/json";

function constantTimeEquals(a: string, b: string): boolean {
  if (a.length !== b.length) {
    return false;
  }
  let result = 0;
  for (let index = 0; index < a.length; index += 1) {
    result |= a.charCodeAt(index) ^ b.charCodeAt(index);
  }
  return result === 0;
}

function authorizeBearer(request: Request, env: Env): boolean {
  const header = request.headers.get("Authorization");
  if (header == null) {
    return false;
  }
  const prefix = "Bearer ";
  if (!header.startsWith(prefix)) {
    return false;
  }
  const presented = header.slice(prefix.length).trim();
  const expected = (env.BAZAARDB_PULL_TOKEN ?? "").trim();
  if (expected.length === 0) {
    return false;
  }
  return constantTimeEquals(presented, expected);
}

export async function handleGetBazaarDbImage(
  request: Request,
  env: Env,
  screenshotId: string,
): Promise<Response> {
  if (!authorizeBearer(request, env)) {
    return new Response(null, { status: 401 });
  }

  if (screenshotId.length === 0) {
    return json({ error: "screenshot_id_required" }, { status: 400 });
  }

  const row = await env.DB.prepare(
    "SELECT r2_key, image_format FROM bazaardb_screenshots WHERE screenshot_id = ?",
  )
    .bind(screenshotId)
    .first<{ r2_key: string; image_format: string }>();

  if (row == null) {
    return new Response(null, { status: 404 });
  }

  const object = await env.BAZAARDB_BUCKET.get(row.r2_key);
  if (object == null) {
    return new Response(null, { status: 404 });
  }

  const contentType =
    row.image_format === "png" ? "image/png" : "application/octet-stream";

  return new Response(object.body, {
    status: 200,
    headers: {
      "content-type": contentType,
      "cache-control": "private, max-age=86400",
    },
  });
}
```

- [ ] **Step 16.3: Commit**

```bash
git add ModCFServerV3/src/features/v3/getBazaarDbImage.ts ModCFServerV3/test/v3.bazaardbScreenshots.test.ts
git commit -m "$(cat <<'EOF'
Add GET /bazaardb/image/{id} R2-proxy handler

Stable URL means revoke = secret rotation, not key rotation. Cache-Control
private max-age=86400 because BazaarDB pulls daily.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 17: Register the three new routes in `src/index.ts`

**Files:**
- Modify: [`ModCFServerV3/src/index.ts`](../../../ModCFServerV3/src/index.ts)
- Modify: [`ModCFServerV3/test/helpers/seed.ts`](../../../ModCFServerV3/test/helpers/seed.ts)

- [ ] **Step 17.1: Wire the static + regex routes**

Replace the relevant blocks in [`ModCFServerV3/src/index.ts`](../../../ModCFServerV3/src/index.ts) so the imports include the new handlers, the static-routes list includes the ingest route, and the dynamic-route matchers handle manifest + image:

Replace lines 1–7 (the import block) with:

```typescript
import type { Env } from "./env";
import { handleCreateReplayLink } from "./features/v3/createReplayLink";
import { handleDownloadReplay } from "./features/v3/downloadReplay";
import { handleGetBazaarDbImage } from "./features/v3/getBazaarDbImage";
import { handleGetBazaarDbManifest } from "./features/v3/getBazaarDbManifest";
import { handleQueryGhostBattles } from "./features/v3/queryGhostBattles";
import { handleUploadBazaarDbScreenshot } from "./features/v3/uploadBazaarDbScreenshot";
import { handleUploadRunBundle } from "./features/v3/uploadRunBundle";
import { preflight, withCors } from "./http/cors";
import { json } from "./http/json";
```

Replace lines 15–19 (`StaticRoutes`) with:

```typescript
const StaticRoutes: StaticRoute[] = [
  { method: "GET", path: "/health", handle: () => json({ ok: true }) },
  { method: "POST", path: "/run-bundles", handle: handleUploadRunBundle },
  { method: "GET", path: "/ghost-battles", handle: handleQueryGhostBattles },
  { method: "POST", path: "/bazaardb-screenshots", handle: handleUploadBazaarDbScreenshot },
  { method: "GET", path: "/bazaardb/manifest", handle: handleGetBazaarDbManifest },
];
```

Just before the closing `return withCors(request, json({ error: "not_found" }, { status: 404 }));` on line 62, add:

```typescript
      const bazaarDbImageMatch = url.pathname.match(/^\/bazaardb\/image\/([^/]+)$/);
      if (request.method === "GET" && bazaarDbImageMatch) {
        return withCors(
          request,
          await handleGetBazaarDbImage(
            request,
            _env,
            decodeURIComponent(bazaarDbImageMatch[1] ?? ""),
          ),
        );
      }

```

- [ ] **Step 17.2: Extend `resetTestState` so the new table + bucket are wiped between tests**

In [`ModCFServerV3/test/helpers/seed.ts`](../../../ModCFServerV3/test/helpers/seed.ts) `resetTestState` (lines 235–246), add the deletes:

```typescript
export async function resetTestState(env: Cloudflare.Env): Promise<void> {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM replay_tokens"),
    env.DB.prepare("DELETE FROM battles"),
    env.DB.prepare("DELETE FROM runs"),
    env.DB.prepare("DELETE FROM run_bundles"),
    env.DB.prepare("DELETE FROM seen_player_accounts"),
    env.DB.prepare("DELETE FROM bazaardb_screenshots"),
  ]);
  await deleteAllR2(env.RUN_BUNDLE_BUCKET);
  await deleteAllR2(env.BAZAARDB_BUCKET);
  env.GHOST_QUERY_LOOKBACK_DAYS = "3";
  env.RUN_BUNDLE_RETENTION_DAYS = "5";
}
```

- [ ] **Step 17.3: Run all server tests**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-mod/ModCFServerV3 && npm test`
Expected: all tests pass, including the new `v3.bazaardbScreenshots.test.ts` suite.

If any of the new tests fail, fix the corresponding handler. Common failure modes to check:
- 400 vs 200 on edge cases — re-read the validation in `handleUploadBazaarDbScreenshot` against the test payload.
- 401 on missing token — verify the `Bearer ` prefix check.
- Empty `image_url` — verify `buildImageUrl` is invoked correctly in the manifest handler.

- [ ] **Step 17.4: Commit**

```bash
git add ModCFServerV3/src/index.ts ModCFServerV3/test/helpers/seed.ts
git commit -m "$(cat <<'EOF'
Register BazaarDB screenshot routes and wipe state between tests

Three new routes: POST /bazaardb-screenshots, GET /bazaardb/manifest,
GET /bazaardb/image/{id}. resetTestState now wipes the new D1 table and
R2 bucket so tests stay isolated.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 18: Run mod + server test suites end-to-end

- [ ] **Step 18.1: Mod tests**

Run, from the mod root:

```bash
dotnet run --project tests/BazaarDbScreenshotUploadStore.Tests/
dotnet run --project tests/BazaarDbScreenshotUploadService.Tests/
dotnet run --project tests/RunScreenshotSqliteStore.Tests/
dotnet run --project tests/RunLoggingSqliteSchema.Tests/
dotnet run --project tests/StartupUploadRunner.Tests/
```

Expected: each prints its `... checks passed.` line and returns 0. The schema test in particular catches regressions from Task 1's schema additions.

- [ ] **Step 18.2: Server tests**

Run: `cd ModCFServerV3 && npm test && npm run check`
Expected: all suites pass, no type errors.

- [ ] **Step 18.3: Commit (no-op if nothing changed)**

If any minor fixes were needed during this verification step, commit them:

```bash
git add -A
git status  # confirm scope is small / fixes-only
git commit -m "$(cat <<'EOF'
Fix issues found in cross-suite verification

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

If there were no changes, skip the commit.

---

## Phase 4 — Installer purge

> **Note**: All paths in Tasks 19–23 are relative to `/Users/yxinyu/codes/bpp/bazaarplusplus-installer/` (the sibling repo). Open a separate shell there before starting these tasks: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer`.

### Task 19: Delete the `bazaardb/` subtree + Tauri command file + frontend `bazaardb/`

**Files:**
- Delete: 11 Rust files in `src-tauri/src/bazaardb/`
- Delete: `src-tauri/src/commands/bazaardb.rs`
- Delete: 4 TypeScript files in `src/lib/bazaardb/`

- [ ] **Step 19.1: Delete the Rust subtree**

```bash
cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer
rm -rf src-tauri/src/bazaardb
rm src-tauri/src/commands/bazaardb.rs
```

- [ ] **Step 19.2: Delete the frontend subtree**

```bash
rm -rf src/lib/bazaardb
```

- [ ] **Step 19.3: Confirm everything is gone**

Run: `find /Users/yxinyu/codes/bpp/bazaarplusplus-installer -type f -path '*bazaardb*' 2>/dev/null`
Expected: only references — no actual files. (References will be cleaned up in Tasks 20 + 21.)

- [ ] **Step 19.4: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Delete installer BazaarDB subtree

Installer is no longer a participant in BazaarDB upload — the mod now
posts directly to ModCFServerV3 and BazaarDB pulls from there.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 20: Prune installer `lib.rs` + `commands/mod.rs` + `installer_db/mod.rs`

**Files (relative to `bazaarplusplus-installer/`):**
- Modify: `src-tauri/src/lib.rs`
- Modify: `src-tauri/src/commands/mod.rs`
- Modify: `src-tauri/src/installer_db/mod.rs`

- [ ] **Step 20.1: Remove `mod bazaardb;`**

Open `src-tauri/src/lib.rs`. Delete the `mod bazaardb;` line (line 1).

- [ ] **Step 20.2: Trim the `commands::{...}` use-list**

In the same file, find the use-list (lines 15–32) and remove the entire `bazaardb::{connect_bazaardb, ..., upload_screenshot_to_bazaardb}` segment, leaving just the other modules:

Before:
```rust
use commands::{
    bazaardb::{connect_bazaardb, disconnect_bazaardb, get_auto_upload_enabled, get_bazaardb_status, list_pending_uploads, set_auto_upload_enabled, upload_screenshot_to_bazaardb},
    bepinex::{...},
    ...
};
```

After:
```rust
use commands::{
    bepinex::{...},
    ...
};
```

- [ ] **Step 20.3: Remove the `spawn_worker` call**

In the same file (around line 66), delete:

```rust
crate::bazaardb::worker::spawn_worker(app.handle().clone());
```

- [ ] **Step 20.4: Remove the deeplink branch**

In the same file (lines 69–91), delete the entire `for url in event.urls() { ... bazaardb::deeplink::parse_link_url ... }` block. If the surrounding `on_open_url` handler becomes empty as a result, remove the whole `app.deep_link().on_open_url(...)` call too. If there are other deep-link consumers in the block, keep the surrounding handler but remove only the bazaardb branch.

- [ ] **Step 20.5: Remove the 7 invoke handlers**

In the same file's `tauri::generate_handler!` macro (around lines 109–116), remove the 7 bazaardb entries:

```rust
connect_bazaardb,
disconnect_bazaardb,
get_bazaardb_status,
upload_screenshot_to_bazaardb,
set_auto_upload_enabled,
get_auto_upload_enabled,
list_pending_uploads,
```

Keep the surrounding `tauri::generate_handler![ ... ]` and the rest of the registered commands.

- [ ] **Step 20.6: Trim `commands/mod.rs`**

Open `src-tauri/src/commands/mod.rs`. Delete the `pub mod bazaardb;` line.

- [ ] **Step 20.7: Drop the orphan SQLite table accessors**

Open `src-tauri/src/installer_db/mod.rs`. Delete:

- The `CREATE TABLE IF NOT EXISTS bazaardb_settings (...)` block (around lines 31–34).
- The `pub fn get_setting(conn: &rusqlite::Connection, key: &str) -> ...` function (around lines 37–47).
- The `pub fn set_setting(conn: &rusqlite::Connection, key: &str, value: &str) -> ...` function (around lines 49–57).

Then add a one-shot drop at installer startup (after the remaining `CREATE TABLE` calls):

```rust
// Clean up the orphan table left behind by the deleted BazaarDB integration.
conn.execute("DROP TABLE IF EXISTS bazaardb_settings", [])
    .map_err(|err| err.to_string())?;
```

- [ ] **Step 20.8: Build cargo**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer && cargo build --manifest-path src-tauri/Cargo.toml`
Expected: build succeeds. Any unresolved symbols here indicate a missed reference to the deleted module — fix and re-run.

- [ ] **Step 20.9: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Prune installer Rust references to BazaarDB

Removes the mod, command bindings, deeplink handler, and the orphan
bazaardb_settings table (with a DROP TABLE migration so existing users'
DBs are tidied up on next launch).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 21: Prune installer frontend

**Files (relative to `bazaarplusplus-installer/`):**
- Modify: `src/lib/bridge/commands.ts`
- Modify: `src/lib/config/endpoints.ts`
- Modify: `src/routes/settings/+page.svelte`
- Modify: `src/lib/components/stream/StreamRecordLibrary.svelte`

- [ ] **Step 21.1: Remove the typed command entries**

Open `src/lib/bridge/commands.ts`. Delete the 7 entries from the typed command map (the exact lines were 25–27 and 115–121 at audit time — re-locate them by searching for `bazaardb`):

```typescript
connect_bazaardb: { input: { request: { token: string } }; output: BazaardbStatus };
disconnect_bazaardb: { input: undefined; output: void };
get_bazaardb_status: { input: undefined; output: BazaardbStatus };
upload_screenshot_to_bazaardb: { input: { request: { screenshot_id: string } }; output: UploadResult; };
set_auto_upload_enabled: { input: { enabled: boolean }; output: void };
get_auto_upload_enabled: { input: undefined; output: boolean };
list_pending_uploads: { input: undefined; output: PendingUploadView[] };
```

Also remove any `BazaardbStatus`, `UploadResult`, `PendingUploadView` type imports/definitions in this file that become unused (the Rust handlers behind them are gone).

- [ ] **Step 21.2: Remove the endpoint constants**

Open `src/lib/config/endpoints.ts`. Delete lines containing:

```typescript
export const BAZAARDB_BASE_URL = 'https://bazaardb.bazaarplusplus.com';
export const BAZAARDB_TOKEN_PAGE_URL = `${BAZAARDB_BASE_URL}/settings/tokens`;
```

- [ ] **Step 21.3: Strip the settings page**

Open `src/routes/settings/+page.svelte`. Make these specific removals (re-locate by search; line numbers may shift between commits):

1. Delete `import { accountStore } from '$lib/bazaardb/account-store';`.
2. In the `onMount` hook, delete the lines that call `accountStore.refresh()`, `call('get_auto_upload_enabled')`, and `call('list_pending_uploads')`.
3. Delete the `connect()` and `disconnect()` async functions (they reference `accountStore.connect` / `accountStore.disconnect`).
4. Delete the entire `<section>` containing the BazaarDB heading, the token input, the connect/disconnect buttons, the auto-upload toggle, and the pending-uploads list (approx lines 138–239 at audit time — locate by the `BazaarDB` text).
5. Remove any now-unused local `let token = '';`, `let busy = false;`, `let pending = ...`, `let autoUpload = ...`, and the related `error` state if it's no longer used elsewhere on the page.
6. Remove now-orphan CSS in the `<style>` block (search for `.bazaardb`, `.pending-upload`, etc., and delete the matching rules).

- [ ] **Step 21.4: Strip the stream record library**

Open `src/lib/components/stream/StreamRecordLibrary.svelte`. Make these removals:

1. Delete the two imports:
   ```typescript
   import { uploadScreenshot } from '$lib/bazaardb/upload-actions';
   import type { UploadStatus } from '$lib/bazaardb/upload-actions';
   ```
2. Delete the state variable: `let uploadStatuses = new Map<string, UploadStatus>();`.
3. Delete the `async function handleUpload(recordId: string) { ... }` function.
4. In the record row JSX (~lines 416–446 at audit time, locate by `record-action-upload`), delete the `<button class="record-action record-action-upload" ...>` element and the conditional status spans (`upload-remote-id`, `upload-queued`, `upload-error`) immediately following it.
5. Remove orphan CSS rules from this file's `<style>` block (search for `.record-action-upload`, `.upload-remote-id`, `.upload-queued`, `.upload-error`).

- [ ] **Step 21.5: Type-check the frontend**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer && npm run check 2>/dev/null || npm run lint 2>/dev/null || npx svelte-check`
Expected: type-check succeeds. (Use whichever script the project defines — `npm run check` is the conventional name; if missing, try `npx svelte-check` directly.)

- [ ] **Step 21.6: Run the existing test suite**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer && npm test 2>&1 | tail -30`
Expected: all remaining tests pass. The `account-store.test.ts` is already deleted by Task 19; no other test references bazaardb.

- [ ] **Step 21.7: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Prune installer frontend references to BazaarDB

Removes the typed Tauri command entries, the BAZAARDB_BASE_URL constant,
the BazaarDB settings section, and the per-record upload button in the
stream library.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 22: Prune installer `Cargo.toml`

**Files:**
- Modify: `src-tauri/Cargo.toml`

- [ ] **Step 22.1: Confirm `keyring` has no other consumer**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer && grep -rn "use keyring\|keyring::" src-tauri/src/ 2>/dev/null`
Expected: no results (all keyring users were in the deleted `bazaardb/` subtree).

If results appear, do NOT remove `keyring` from Cargo.toml — investigate first.

- [ ] **Step 22.2: Confirm `reqwest`'s `multipart` feature has no other consumer**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer && grep -rn "multipart::Form\|reqwest::multipart" src-tauri/src/ 2>/dev/null`
Expected: no results.

If results appear, leave the feature in place.

- [ ] **Step 22.3: Remove `keyring` line and trim `reqwest` features**

Open `src-tauri/Cargo.toml`. Delete:

```toml
keyring = "3"
```

If verified clean above, change:

```toml
reqwest = { version = "0.12", default-features = false, features = ["rustls-tls", "multipart", "json"] }
```

to:

```toml
reqwest = { version = "0.12", default-features = false, features = ["rustls-tls", "json"] }
```

- [ ] **Step 22.4: Rebuild Rust**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer && cargo build --manifest-path src-tauri/Cargo.toml`
Expected: build succeeds, Cargo.lock updates removing keyring + multipart-related transitives.

If the build fails, revert the relevant change and investigate (one of the verification greps may have missed a usage).

- [ ] **Step 22.5: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Drop keyring crate and reqwest multipart feature from installer

Both were exclusive to the deleted BazaarDB upload path. OS keychain
entries written by the old keyring code remain inert until the user
clears them manually.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

### Task 23: Final installer smoke

- [ ] **Step 23.1: Full build**

Run, from the installer repo root:

```bash
cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer
cargo build --manifest-path src-tauri/Cargo.toml
npm run build 2>&1 | tail -30
npm test 2>&1 | tail -10
```

Expected: cargo build succeeds; frontend build succeeds; tests pass.

- [ ] **Step 23.2: Search for stragglers**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-installer && grep -rn -i "bazaardb" --include='*.rs' --include='*.ts' --include='*.svelte' --include='*.toml' src-tauri/ src/ Cargo.toml 2>/dev/null`
Expected: zero matches. (The about-page link `bazaardb.gg` lives in `src/lib/about/content.ts` and was intentionally preserved per design §9.3 — it's a project link, not an upload path. The grep above scopes to `src-tauri/` and `src/`, which excludes about content if it's in a different location; if it surfaces a hit there, leave it.)

- [ ] **Step 23.3: No-op commit if needed**

If any stragglers were found and fixed:

```bash
git add -A
git commit -m "$(cat <<'EOF'
Clean up final BazaarDB references in installer

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

If nothing else was found, skip.

---

## Phase 5 — Final verification

### Task 24: Run mod `BuildAll`

- [ ] **Step 24.1: BuildAll**

Run, from the mod root:

```bash
cd /Users/yxinyu/codes/bpp/bazaarplusplus-mod
dotnet build BazaarPlusPlus.csproj /t:BuildAll
```

Expected: both Debug and Release configurations build cleanly. The Debug build copies the DLL to `BepInEx/plugins/`; the Release build copies into the installer's `SourceForBuild/` (this is harmless — the installer ships the mod even though it has stopped owning BazaarDB upload).

- [ ] **Step 24.2: Full mod test sweep**

Run, from the mod root:

```bash
for t in tests/*.Tests/; do
  dotnet run --project "$t" 2>&1 | tail -3
done
```

Expected: each test project prints its `... checks passed.` line. (This catches accidental regressions in other test projects from the schema-version bump in Task 1.)

- [ ] **Step 24.3: Server full sweep**

Run: `cd /Users/yxinyu/codes/bpp/bazaarplusplus-mod/ModCFServerV3 && npm run check && npm test`
Expected: passes.

- [ ] **Step 24.4: Final commit (if any new fixes)**

```bash
git add -A
git status  # confirm scope is small / fixes-only
git diff --cached --stat
git commit -m "$(cat <<'EOF'
Fix issues found in final cross-suite verification

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

Skip if no changes.

---

## Out-of-band steps (operator, not agent)

These actions are required for the feature to actually function but are explicitly **not** part of this plan because they require operator credentials:

1. **Apply D1 migration**: `cd ModCFServerV3 && npx wrangler d1 migrations apply bazaarplusplus-mod-api-v3-db --remote`
2. **Provision R2 bucket** (one-time): `npx wrangler r2 bucket create bazaarplusplus-bazaardb-assets`
3. **Provision the BazaarDB pull token**: `npx wrangler secret put BAZAARDB_PULL_TOKEN` and share the value out-of-band with the BazaarDB team.
4. **Deploy the Worker**: `npx wrangler deploy`
5. **Cut a mod release** (default config OFF — no user impact) and observe `mod-api-v3.bazaarplusplus.com/bazaardb-screenshots` logs for early-adopter traffic.
6. **Notify BazaarDB** that the new manifest endpoint is live and they can begin cron-polling.
7. **Cut an installer release** after the mod has rolled out widely (design §12 recommends 1–2 mod releases as a grace period).

---

## Risks & rollback

| Risk | Mitigation |
|---|---|
| Schema bump to 13 breaks an older mod build sharing the same DB | `CREATE TABLE IF NOT EXISTS` is idempotent; older mods never read the new table. Worst case: warning logs from foreign-key references. |
| Worker R2 quota exhaustion | PNGs at ~1–3MB × low daily volume × Cloudflare R2 quotas — the math is benign for v1. Monitor `BAZAARDB_BUCKET` size in Cloudflare dashboard. |
| Pull token leaks | Rotate via `wrangler secret put BAZAARDB_PULL_TOKEN` — image URLs stay stable because they're Worker-proxied. |
| Mod uploads stale screenshots from past sessions | This is intentional per design §6.3 (toggle-on backfill). If a user objects, the only remedy is to disable the toggle; we never delete past uploads. |
| Installer release shipped before mod release picks up adoption | Acceptable gap — BazaarDB just sees fewer screenshots during the window. No data loss. |

To roll back the **mod** change: revert the commits or ship a release with `BazaarDbUploadEnabled` default unchanged (off). No data needs to be cleaned.
To roll back the **server** change: leave the routes deployed; they're inert when the mod doesn't post.
To roll back the **installer** change: revert the deletion commits. The OS keychain entries from the old `keyring.rs` are still there for older users.
