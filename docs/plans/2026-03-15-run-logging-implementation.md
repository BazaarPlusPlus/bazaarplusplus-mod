# Run Logging Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a standalone `RunLogging` module that records per-run encounter and choice history behind a storage interface, with the first working backend implemented as JSON files.

**Architecture:** Introduce a storage-agnostic run logging domain under `Game/RunLogging` with canonical DTOs, a session manager, a capture service, and an `IRunLogStore` boundary. Implement `JsonRunLogStore` first using one directory per `run_id`, append-only `events.ndjson`, atomic checkpoints, and active-run recovery. Delay SQLite implementation, but keep interface and model boundaries stable so a `SqliteRunLogStore` can be added later without touching capture or inference logic.

**Tech Stack:** C# 12, .NET `netstandard2.1`, BepInEx, Harmony, Newtonsoft.Json, local filesystem persistence, console-style linked-source tests

---

### Task 1: Define canonical persistence models and the storage interface

**Files:**
- Create: `Game/RunLogging/Models/RunLogCreateRequest.cs`
- Create: `Game/RunLogging/Models/RunLogSessionState.cs`
- Create: `Game/RunLogging/Models/RunLogEvent.cs`
- Create: `Game/RunLogging/Models/RunLogCheckpoint.cs`
- Create: `Game/RunLogging/Models/RunLogCompletion.cs`
- Create: `Game/RunLogging/Models/RunLogAbandonment.cs`
- Create: `Game/RunLogging/Persistence/IRunLogStore.cs`
- Create: `tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`
- Create: `tests/RunLoggingModels.Tests/Program.cs`

**Step 1: Write the failing test**

Create a small console test that uses reflection to assert:

- all canonical persistence model types exist in `BazaarPlusPlus`
- `IRunLogStore` exists
- `IRunLogStore` exposes:
  - `TryResumeActiveRun`
  - `CreateRun`
  - `AppendEvent`
  - `SaveCheckpoint`
  - `CompleteRun`
  - `MarkRunAbandoned`

The test should also assert that `RunLogEvent` exposes stable envelope fields:

- `SchemaVersion`
- `RunId`
- `Seq`
- `Ts`
- `Kind`

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Expected: FAIL because the new `RunLogging` types and interface do not exist yet.

**Step 3: Write minimal implementation**

Create the new model files and `IRunLogStore` with the exact methods the test expects. Keep these types backend-agnostic and avoid JSON- or SQLite-specific members.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/Models/*.cs \
  Game/RunLogging/Persistence/IRunLogStore.cs \
  tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj \
  tests/RunLoggingModels.Tests/Program.cs
git commit -m "feat: add run logging persistence contract"
```

### Task 2: Add JSON schema helpers and run id generation

**Files:**
- Create: `Game/RunLogging/Json/RunLogJsonSchema.cs`
- Create: `Game/RunLogging/RunIdFactory.cs`
- Create: `tests/RunLoggingJsonSchema.Tests/RunLoggingJsonSchema.Tests.csproj`
- Create: `tests/RunLoggingJsonSchema.Tests/Program.cs`

**Step 1: Write the failing test**

Create a console test that asserts:

- `RunIdFactory` can produce a stable `run_id` format beginning with `run_`
- generated ids include readable time/hero/mode fragments
- `RunLogJsonSchema` exposes:
  - `CurrentSchemaVersion`
  - known file names: `meta.json`, `events.ndjson`, `checkpoint.json`, `status.json`, `active-run.json`, `runs-index.json`

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingJsonSchema.Tests/RunLoggingJsonSchema.Tests.csproj`

Expected: FAIL because the schema helper and run id factory do not exist yet.

**Step 3: Write minimal implementation**

Implement:

- `RunLogJsonSchema` as a single source of truth for file names and schema version
- `RunIdFactory` that builds readable ids from start time, hero, mode, day/hour seed, and a nonce

Keep hashing and string normalization small and deterministic.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingJsonSchema.Tests/RunLoggingJsonSchema.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/Json/RunLogJsonSchema.cs \
  Game/RunLogging/RunIdFactory.cs \
  tests/RunLoggingJsonSchema.Tests/RunLoggingJsonSchema.Tests.csproj \
  tests/RunLoggingJsonSchema.Tests/Program.cs
git commit -m "feat: add run logging json schema helpers"
```

### Task 3: Implement path layout and JSON file naming

**Files:**
- Create: `Game/RunLogging/Json/RunLogPathLayout.cs`
- Create: `tests/RunLoggingPathLayout.Tests/RunLoggingPathLayout.Tests.csproj`
- Create: `tests/RunLoggingPathLayout.Tests/Program.cs`

**Step 1: Write the failing test**

Create a console test that asserts `RunLogPathLayout` can derive:

- base log root
- date partition
- run directory path
- paths to:
  - `meta.json`
  - `events.ndjson`
  - `checkpoint.json`
  - `status.json`
- root helper files:
  - `active-run.json`
  - `runs-index.json`

The test should verify the expected `<date>/<run_id>/...` shape.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingPathLayout.Tests/RunLoggingPathLayout.Tests.csproj`

Expected: FAIL because the path layout helper does not exist yet.

**Step 3: Write minimal implementation**

Implement `RunLogPathLayout` as a pure path builder. Do not perform any file I/O in this type.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingPathLayout.Tests/RunLoggingPathLayout.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/Json/RunLogPathLayout.cs \
  tests/RunLoggingPathLayout.Tests/RunLoggingPathLayout.Tests.csproj \
  tests/RunLoggingPathLayout.Tests/Program.cs
git commit -m "feat: add run logging path layout"
```

### Task 4: Implement JSON store write path for create, append, checkpoint, and completion

**Files:**
- Create: `Game/RunLogging/Persistence/JsonRunLogStore.cs`
- Create: `tests/RunLoggingJsonStore.Tests/RunLoggingJsonStore.Tests.csproj`
- Create: `tests/RunLoggingJsonStore.Tests/Program.cs`

**Step 1: Write the failing test**

Create a temp-directory-based console test that:

1. creates a `JsonRunLogStore`
2. calls `CreateRun`
3. appends two events
4. saves a checkpoint
5. completes the run

Then assert:

- the run directory exists
- `meta.json` exists and contains the right `run_id`
- `events.ndjson` has exactly two lines
- `checkpoint.json` contains the right `last_seq`
- `status.json` contains terminal status
- `active-run.json` is updated appropriately during the session

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingJsonStore.Tests/RunLoggingJsonStore.Tests.csproj`

Expected: FAIL because `JsonRunLogStore` does not exist yet.

**Step 3: Write minimal implementation**

Implement `JsonRunLogStore` with:

- directory creation
- `meta.json` write
- append-only `events.ndjson`
- atomic `checkpoint.json` write via temp file + replace
- atomic `status.json` write via temp file + replace
- root `active-run.json` maintenance

Keep this backend self-contained and do not mix inference logic into it.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingJsonStore.Tests/RunLoggingJsonStore.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/Persistence/JsonRunLogStore.cs \
  tests/RunLoggingJsonStore.Tests/RunLoggingJsonStore.Tests.csproj \
  tests/RunLoggingJsonStore.Tests/Program.cs
git commit -m "feat: implement json run log store"
```

### Task 5: Implement active-run recovery and abandonment handling

**Files:**
- Modify: `Game/RunLogging/Persistence/JsonRunLogStore.cs`
- Create: `tests/RunLoggingRecovery.Tests/RunLoggingRecovery.Tests.csproj`
- Create: `tests/RunLoggingRecovery.Tests/Program.cs`

**Step 1: Write the failing test**

Create a temp-directory-based console test that:

1. creates a run
2. appends events and saves a checkpoint
3. constructs a fresh `JsonRunLogStore`
4. calls `TryResumeActiveRun`

Assert that resume restores:

- `run_id`
- `last_seq`
- checkpoint day/hour/state
- dedupe anchors stored in checkpoint

Then add a second check that `MarkRunAbandoned` writes terminal status and clears active-run tracking.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingRecovery.Tests/RunLoggingRecovery.Tests.csproj`

Expected: FAIL because recovery/abandonment behavior is incomplete.

**Step 3: Write minimal implementation**

Extend `JsonRunLogStore` to:

- read `active-run.json`
- load checkpoint and metadata
- return `RunLogSessionState` for unfinished runs
- support `MarkRunAbandoned`

Keep recovery logic conservative; if helper files are inconsistent, prefer marking the run stale over inventing state.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingRecovery.Tests/RunLoggingRecovery.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/Persistence/JsonRunLogStore.cs \
  tests/RunLoggingRecovery.Tests/RunLoggingRecovery.Tests.csproj \
  tests/RunLoggingRecovery.Tests/Program.cs
git commit -m "feat: add run logging recovery support"
```

### Task 6: Add session manager and in-memory sequencing/dedupe state

**Files:**
- Create: `Game/RunLogging/RunLogSessionManager.cs`
- Create: `tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
- Create: `tests/RunLoggingSession.Tests/Program.cs`

**Step 1: Write the failing test**

Create a console test using a fake in-memory `IRunLogStore` that asserts:

- a new session creates a run only once
- `seq` increments monotonically
- duplicate `selection_seen` fingerprints are suppressed
- completion closes the active session
- resume hydrates the next sequence from persisted session state

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`

Expected: FAIL because `RunLogSessionManager` does not exist yet.

**Step 3: Write minimal implementation**

Implement `RunLogSessionManager` as the active in-memory coordinator for:

- create/resume
- sequence assignment
- last known day/hour/state
- last state fingerprint
- last selection fingerprint
- pending selection sequence

Do not place file I/O in this type; use `IRunLogStore`.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/RunLogSessionManager.cs \
  tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj \
  tests/RunLoggingSession.Tests/Program.cs
git commit -m "feat: add run logging session manager"
```

### Task 7: Add capture helpers for run/state/selection snapshots

**Files:**
- Create: `Game/RunLogging/RunLogCaptureService.cs`
- Create: `Game/RunLogging/RunLogSnapshotBuilder.cs`
- Create: `tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
- Create: `tests/RunLoggingCapture.Tests/Program.cs`

**Step 1: Write the failing test**

Create a console test around pure helper methods that asserts:

- run/state inputs can be converted into canonical `run_progress` and `state_seen` events
- selection inputs produce deterministic `selection_fingerprint`
- option projection preserves:
  - `instance_id`
  - `template_id`
  - `name`
  - `tier`
  - `enchant`

Keep this test independent from live Unity event wiring.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: FAIL because capture helpers do not exist yet.

**Step 3: Write minimal implementation**

Implement small pure helpers first:

- build run progress records
- build state records
- build selection records from resolved option data
- compute fingerprints

Leave live message/event subscription out of scope for this task.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/RunLogCaptureService.cs \
  Game/RunLogging/RunLogSnapshotBuilder.cs \
  tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj \
  tests/RunLoggingCapture.Tests/Program.cs
git commit -m "feat: add run logging capture helpers"
```

### Task 8: Add conservative choice inference

**Files:**
- Create: `Game/RunLogging/RunLogInferenceService.cs`
- Create: `tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj`
- Create: `tests/RunLoggingInference.Tests/Program.cs`

**Step 1: Write the failing test**

Create a console test that covers small deterministic cases:

1. a single-option selection transitions away and should infer that option as chosen
2. a previously visible option appears in resulting player state and should infer that option
3. an ambiguous transition should return no precise choice

Assert the resulting canonical `choice_made` fields:

- `selection_seq`
- `selected_instance_id`
- `inferred_from`
- `confidence`

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj`

Expected: FAIL because the inference service does not exist yet.

**Step 3: Write minimal implementation**

Implement `RunLogInferenceService` with conservative heuristics only. Prefer missing a choice over writing a wrong high-confidence result.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/RunLogInferenceService.cs \
  tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj \
  tests/RunLoggingInference.Tests/Program.cs
git commit -m "feat: add run logging choice inference"
```

### Task 9: Add the runtime controller and mount the module in the plugin

**Files:**
- Create: `Game/RunLogging/RunLoggingController.cs`
- Modify: `Plugin.cs`
- Modify: `Models/ModState.cs`
- Test: `tests/RunLoggingModels.Tests/Program.cs`

**Step 1: Write the failing test**

Extend the existing reflection-style test or add a focused assertion that:

- `RunLoggingController` exists
- `Plugin` mounts it via `gameObject.AddComponent<RunLoggingController>();`

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Expected: FAIL because the controller is not mounted yet.

**Step 3: Write minimal implementation**

Implement `RunLoggingController` as the runtime entry point that:

- creates the JSON store
- creates or resumes the session manager
- exposes small methods to accept captured updates

Then mount it from `Plugin.Awake()`. If configuration is needed, add only the smallest new state needed in `ModState`.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/RunLogging/RunLoggingController.cs \
  Plugin.cs \
  Models/ModState.cs \
  tests/RunLoggingModels.Tests/Program.cs
git commit -m "feat: mount run logging module"
```

### Task 10: Wire the minimum live capture loop

**Files:**
- Modify: `Game/RunLogging/RunLoggingController.cs`
- Modify: `Game/RunStateSyncController.cs`
- Modify: `Game/GameDataReader.cs`
- Modify: `Game/EncounterTracker.cs`
- Create: `docs/2026-03-15-run-logging-verification.md`

**Step 1: Write the failing test**

Add or extend a pure test around the controller/session seam so it asserts the module can accept:

- run progress update
- state snapshot
- selection snapshot

and forward them into event writes in the expected order.

If a pure automated test is not enough for the final Unity glue, document the exact manual verification steps before implementing.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: FAIL until the live glue path forwards real snapshots to the logging module.

**Step 3: Write minimal implementation**

Wire the smallest end-to-end loop:

- observe run activity changes
- capture current run day/hour
- capture current state and encounter id
- resolve selection options when a supported selection state is visible
- emit `run_started`, `state_seen`, `selection_seen`, and `run_completed`

Keep `choice_made` emission conservative and only enable it where the inference inputs are already available.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: PASS for the pure seam tests.

Then run a broader compile verification:

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: build succeeds if local game references are available.

Then perform manual runtime verification and record results in `docs/2026-03-15-run-logging-verification.md`:

- start a run
- verify a new `run_id` directory is created
- verify `meta.json` exists
- enter an encounter/choice state and verify `events.ndjson` appends `selection_seen`
- force-close or stop before run completion and verify recovery on next launch
- finish or abandon a run and verify `status.json`

**Step 5: Commit**

```bash
git add Game/RunLogging/RunLoggingController.cs \
  Game/RunStateSyncController.cs \
  Game/GameDataReader.cs \
  Game/EncounterTracker.cs \
  docs/2026-03-15-run-logging-verification.md
git commit -m "feat: wire minimum live run logging"
```

### Task 11: Verify the full first-phase JSON backend

**Files:**
- Verify: `Game/RunLogging/**/*.cs`
- Verify: `tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`
- Verify: `tests/RunLoggingJsonSchema.Tests/RunLoggingJsonSchema.Tests.csproj`
- Verify: `tests/RunLoggingPathLayout.Tests/RunLoggingPathLayout.Tests.csproj`
- Verify: `tests/RunLoggingJsonStore.Tests/RunLoggingJsonStore.Tests.csproj`
- Verify: `tests/RunLoggingRecovery.Tests/RunLoggingRecovery.Tests.csproj`
- Verify: `tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
- Verify: `tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
- Verify: `tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj`
- Verify: `BazaarPlusPlus.csproj`

**Step 1: Run targeted tests**

Run:

```bash
dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj
dotnet run --project tests/RunLoggingJsonSchema.Tests/RunLoggingJsonSchema.Tests.csproj
dotnet run --project tests/RunLoggingPathLayout.Tests/RunLoggingPathLayout.Tests.csproj
dotnet run --project tests/RunLoggingJsonStore.Tests/RunLoggingJsonStore.Tests.csproj
dotnet run --project tests/RunLoggingRecovery.Tests/RunLoggingRecovery.Tests.csproj
dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj
dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj
dotnet run --project tests/RunLoggingInference.Tests/RunLoggingInference.Tests.csproj
```

Expected: all PASS.

**Step 2: Run compile verification**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: PASS if local game assembly references are available; otherwise document the environment limitation.

**Step 3: Commit**

```bash
git add .
git commit -m "chore: verify run logging json backend"
```
