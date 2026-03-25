# Run Logging And History UI Perf Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove the highest-confidence main-thread stalls introduced after `v1.1.0` without changing run-log semantics.

**Architecture:** Treat duplicate `state_seen` snapshots as a normal no-op instead of an exceptional path, so the 250ms sync loop stops generating exceptions and error logs under stable game state. Cache the History collections anchor and only rescan the full UI tree when the cached reference is no longer valid, preserving behavior while removing unconditional periodic full-scene scans.

**Tech Stack:** C#, Unity MonoBehaviours, custom executable tests

---

### Task 1: Duplicate `state_seen` suppression

**Files:**
- Modify: `tests/RunLoggingCapture.Tests/Program.cs`
- Modify: `Game/RunLogging/RunLoggingController.cs`
- Test: `tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

**Step 1: Write the failing test**

Add a controller-core seam assertion that a second identical `AcceptStateSnapshot` call returns `null` and does not append a second event.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
Expected: FAIL before implementation because duplicate state snapshots currently throw.

**Step 3: Write minimal implementation**

Change `AcceptStateSnapshot` to return `RunLogEvent?` and treat duplicate suppression as a normal no-op.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
Expected: PASS

### Task 2: History collections anchor caching

**Files:**
- Modify: `Game/HistoryPanel/HistoryCollectionsEntryBridge.cs`

**Step 1: Implement cached anchor lookup**

Store the last valid anchor/parent and skip `Resources.FindObjectsOfTypeAll<Button>()` while the cached entry remains valid.

**Step 2: Verify behavior still works**

Run focused source/build verification alongside the run-logging test suite.

### Task 3: Focused verification

**Files:**
- Test: `tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
- Test: `tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

**Step 1: Run focused verification**

Run:
- `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
- `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

**Step 2: Summarize residual risk**

Call out that History anchor caching does not yet have direct automated coverage if no focused test seam exists.
