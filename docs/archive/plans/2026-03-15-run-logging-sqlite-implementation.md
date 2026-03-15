# Run Logging SQLite-Only Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace runtime JSON persistence with a SQLite-backed run logging store, while adding a Python export tool that emits the existing JSON debug layout from SQLite on demand.

**Architecture:** Keep the existing canonical run logging DTOs and store boundary in C#, but switch runtime persistence to a single `SqliteRunLogStore` implementation. Remove JSON runtime persistence code from C#, keep controller/session/capture/inference storage-agnostic, and add a Python export script that reads SQLite and writes JSON debug files.

**Tech Stack:** C# 12, .NET `netstandard2.1`, BepInEx, Harmony, `Microsoft.Data.Sqlite`, SQLite, Python 3, console-style linked-source tests

---

### Task 1: Add SQLite dependency and schema bootstrap

**Files:**
- Modify: `BazaarPlusPlus.csproj`
- Create: `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs`
- Create: `tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj`
- Create: `tests/RunLoggingSqliteSchema.Tests/Program.cs`

**Step 1: Write the failing test**

Create a console test that asserts:

- `RunLogSqliteSchema` exists
- it exposes a database file name constant
- it exposes the table names:
  - `runs`
  - `run_events`
  - `run_checkpoints`
  - `run_status`
- it exposes SQL schema bootstrap text or methods sufficient to initialize the database

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj`

Expected: FAIL because the SQLite schema helper does not exist yet.

**Step 3: Write minimal implementation**

Implement:

- the SQLite package reference in `BazaarPlusPlus.csproj`
- `RunLogSqliteSchema` as the single source of truth for database file name, table names, and schema bootstrap SQL

Keep schema bootstrap logic self-contained and deterministic.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add BazaarPlusPlus.csproj \
  Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs \
  tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj \
  tests/RunLoggingSqliteSchema.Tests/Program.cs
git commit -m "feat: add run logging sqlite schema bootstrap"
```

### Task 2: Implement SQLite store create, append, checkpoint, and completion

**Files:**
- Create: `Game/RunLogging/Persistence/SqliteRunLogStore.cs`
- Create: `tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`
- Create: `tests/RunLoggingSqliteStore.Tests/Program.cs`

**Step 1: Write the failing test**

Create a temp-database-based console test that:

1. creates a `SqliteRunLogStore`
2. calls `CreateRun`
3. appends two events
4. saves a checkpoint
5. completes the run

Then assert:

- the database file exists
- `runs` contains the created run
- `run_events` contains exactly two rows with the expected `seq`
- `run_checkpoints` contains the expected `last_seq`
- `run_status` contains terminal status

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`

Expected: FAIL because `SqliteRunLogStore` does not exist yet.

**Step 3: Write minimal implementation**

Implement `SqliteRunLogStore` with:

- schema initialization
- parameterized inserts/updates
- transactional create/checkpoint/complete writes
- canonical event payload storage in `payload_json`

Do not add JSON export logic to this type.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/Persistence/SqliteRunLogStore.cs \
  tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj \
  tests/RunLoggingSqliteStore.Tests/Program.cs
git commit -m "feat: implement sqlite run log store writes"
```

### Task 3: Implement SQLite recovery and abandonment handling

**Files:**
- Modify: `Game/RunLogging/Persistence/SqliteRunLogStore.cs`
- Create: `tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj`
- Create: `tests/RunLoggingSqliteRecovery.Tests/Program.cs`

**Step 1: Write the failing test**

Create a temp-database-based console test that:

1. creates a run
2. appends events and saves a checkpoint
3. constructs a fresh `SqliteRunLogStore`
4. calls `TryResumeActiveRun`
5. calls `MarkRunAbandoned`

Then assert:

- resume returns the unfinished run
- restored state preserves:
  - `run_id`
  - `last_seq`
  - day/hour/state
  - dedupe anchors
  - pending selection sequence
- abandonment writes `run_status`
- abandoned runs are no longer returned by resume

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj`

Expected: FAIL because recovery/abandonment are incomplete.

**Step 3: Write minimal implementation**

Extend `SqliteRunLogStore` to:

- query unfinished runs conservatively
- prefer the newest unfinished checkpoint
- map checkpoint rows back into `RunLogSessionState`
- persist abandonment into `run_status`

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/Persistence/SqliteRunLogStore.cs \
  tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj \
  tests/RunLoggingSqliteRecovery.Tests/Program.cs
git commit -m "feat: add sqlite run log recovery"
```

### Task 4: Switch runtime wiring to SQLite and remove JSON runtime persistence

**Files:**
- Modify: `Game/RunLogging/RunLoggingController.cs`
- Modify: `Models/ModState.cs`
- Delete: `Game/RunLogging/Persistence/JsonRunLogStore.cs`
- Delete: `Game/RunLogging/Json/RunLogJsonSchema.cs`
- Delete: `Game/RunLogging/Json/RunLogPathLayout.cs`
- Modify: `tests/RunLoggingModels.Tests/Program.cs`
- Delete: `tests/RunLoggingJsonSchema.Tests/RunLoggingJsonSchema.Tests.csproj`
- Delete: `tests/RunLoggingJsonSchema.Tests/Program.cs`
- Delete: `tests/RunLoggingPathLayout.Tests/RunLoggingPathLayout.Tests.csproj`
- Delete: `tests/RunLoggingPathLayout.Tests/Program.cs`
- Delete: `tests/RunLoggingJsonStore.Tests/RunLoggingJsonStore.Tests.csproj`
- Delete: `tests/RunLoggingJsonStore.Tests/Program.cs`
- Delete: `tests/RunLoggingRecovery.Tests/RunLoggingRecovery.Tests.csproj`
- Delete: `tests/RunLoggingRecovery.Tests/Program.cs`

**Step 1: Write the failing test**

Extend the existing reflection/source-style test to assert:

- `RunLoggingController` creates `SqliteRunLogStore`
- the runtime no longer references `JsonRunLogStore`
- deleted JSON runtime persistence files are no longer present

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Expected: FAIL until runtime wiring is switched to SQLite and JSON runtime persistence is removed.

**Step 3: Write minimal implementation**

Switch runtime construction to SQLite and remove the JSON runtime persistence classes and tests.

Keep:

- canonical DTOs
- `RunIdFactory`
- capture/session/inference/controller core

Remove only JSON runtime persistence concerns.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/RunLoggingController.cs \
  Models/ModState.cs \
  tests/RunLoggingModels.Tests/Program.cs
git rm Game/RunLogging/Persistence/JsonRunLogStore.cs \
  Game/RunLogging/Json/RunLogJsonSchema.cs \
  Game/RunLogging/Json/RunLogPathLayout.cs \
  tests/RunLoggingJsonSchema.Tests/RunLoggingJsonSchema.Tests.csproj \
  tests/RunLoggingJsonSchema.Tests/Program.cs \
  tests/RunLoggingPathLayout.Tests/RunLoggingPathLayout.Tests.csproj \
  tests/RunLoggingPathLayout.Tests/Program.cs \
  tests/RunLoggingJsonStore.Tests/RunLoggingJsonStore.Tests.csproj \
  tests/RunLoggingJsonStore.Tests/Program.cs \
  tests/RunLoggingRecovery.Tests/RunLoggingRecovery.Tests.csproj \
  tests/RunLoggingRecovery.Tests/Program.cs
git commit -m "refactor: switch run logging runtime to sqlite"
```

### Task 5: Add Python export tool for JSON debug output

**Files:**
- Create: `scripts/export_run_log.py`
- Create: `tests/RunLoggingExport.Tests/RunLoggingExport.Tests.csproj`
- Create: `tests/RunLoggingExport.Tests/Program.cs`

**Step 1: Write the failing test**

Create a console test that:

1. creates a temp SQLite database through `SqliteRunLogStore`
2. writes a run, events, checkpoint, and status
3. invokes `python3 scripts/export_run_log.py --db <path> --run-id <id> --out <dir>`
4. invokes `python3 scripts/export_run_log.py --db <path> --all --out <dir>`

Then assert:

- the output directory exists
- `meta.json` exists
- `events.ndjson` contains the expected number of lines
- `checkpoint.json` and `status.json` match the expected run
- `--all` writes one export directory per run in the database

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingExport.Tests/RunLoggingExport.Tests.csproj`

Expected: FAIL because the Python export tool does not exist yet.

**Step 3: Write minimal implementation**

Implement `scripts/export_run_log.py` to:

- open the SQLite database
- fetch canonical rows for one `run_id` or every run when `--all` is provided
- write the JSON debug files using the same field names already used by the DTO serialization

Keep this tool read-only.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingExport.Tests/RunLoggingExport.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add scripts/export_run_log.py \
  tests/RunLoggingExport.Tests/RunLoggingExport.Tests.csproj \
  tests/RunLoggingExport.Tests/Program.cs
git commit -m "feat: add sqlite run log export tool"
```

### Task 6: Verify the SQLite-only backend end to end

**Files:**
- Verify: `Game/RunLogging/**/*.cs`
- Verify: `tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`
- Verify: `tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj`
- Verify: `tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`
- Verify: `tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj`
- Verify: `tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
- Verify: `tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
- Verify: `tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj`
- Verify: `tests/RunLoggingExport.Tests/RunLoggingExport.Tests.csproj`
- Verify: `BazaarPlusPlus.csproj`

**Step 1: Run targeted tests**

Run:

```bash
dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj
dotnet run --project tests/RunLoggingSqliteSchema.Tests/RunLoggingSqliteSchema.Tests.csproj
dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj
dotnet run --project tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj
dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj
dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj
dotnet run --project tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj
dotnet run --project tests/RunLoggingExport.Tests/RunLoggingExport.Tests.csproj
```

Expected: all PASS.

**Step 2: Run compile verification**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: PASS if local game assembly references are available.

**Step 3: Commit**

```bash
git add .
git commit -m "chore: verify sqlite run logging backend"
```
