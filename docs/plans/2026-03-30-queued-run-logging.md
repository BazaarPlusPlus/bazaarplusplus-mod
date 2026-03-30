# Queued Run Logging Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Move non-terminal run logging SQLite writes off the Unity main thread while preserving synchronous durability for run completion.

**Architecture:** Keep `SqliteRunLogStore` as the low-level synchronous writer and add a queued wrapper that serializes writes on a background worker. `CreateRun`, `AppendEvent`, `SaveCheckpoint`, and `MarkRunAbandoned` enqueue work, while `CompleteRun` first drains queued work for that run and then writes terminal status synchronously so `run_completed` remains durable.

**Tech Stack:** C#, .NET, `Microsoft.Data.Sqlite`, existing run logging tests under `tests/`

---

### Task 1: Add focused persistence tests

**Files:**
- Modify: `tests/RunUploadSync.Tests/Program.cs`
- Modify: `tests/RunLoggingSession.Tests/Program.cs`

**Step 1: Write the failing test**

Add one test that verifies the queued store marks runs dirty only after the worker flushes, and one test that verifies `CompleteRun` drains pending writes before writing terminal status.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj`
Expected: FAIL because the queued store does not exist yet.

Run: `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
Expected: FAIL because synchronous completion-drain behavior does not exist yet.

**Step 3: Write minimal implementation**

Add the queued wrapper and expose any small test seams needed without broadening production APIs more than necessary.

**Step 4: Run test to verify it passes**

Run the same two commands again.
Expected: PASS.

### Task 2: Implement queued run log store

**Files:**
- Create: `Game/RunLogging/Persistence/QueuedRunLogStore.cs`
- Modify: `Game/RunLogging/Persistence/IRunLogStore.cs`
- Modify: `Game/RunLogging/Persistence/ReplicatedRunLogStore.cs`

**Step 1: Write the failing test**

Cover queue ordering, flush/drain semantics, and shutdown behavior through the focused tests from Task 1.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj`
Expected: FAIL on missing type/behavior.

**Step 3: Write minimal implementation**

Implement a single-worker queue that:
- serializes non-terminal writes in order
- coalesces dirty marking into the same worker operation
- exposes a bounded drain path used by completion and teardown

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj`
Expected: PASS.

### Task 3: Wire the queued store into runtime lifecycle

**Files:**
- Modify: `Game/RunLogging/RunLoggingController.cs`

**Step 1: Write the failing test**

Use the focused tests to require disposal/drain behavior instead of adding a new integration-heavy test seam.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
Expected: FAIL until controller/store lifecycle is updated.

**Step 3: Write minimal implementation**

Instantiate the queued store in the controller and dispose it during teardown so pending non-terminal writes are flushed best-effort.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
Expected: PASS.

### Task 4: Verify targeted regressions

**Files:**
- Test: `tests/RunUploadSync.Tests/Program.cs`
- Test: `tests/RunLoggingSession.Tests/Program.cs`
- Test: `tests/RunLoggingSqliteStore.Tests/Program.cs`

**Step 1: Run focused verification**

Run:
- `dotnet run --project tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj`
- `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
- `dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`

Expected: PASS for all three projects.

**Step 2: Inspect behavioral risks**

Confirm:
- non-terminal writes no longer synchronously hit SQLite on the call site
- `CompleteRun` persists all prior queued writes for the same run before returning
- teardown drain remains best-effort and bounded
