# Combat Replay Orphan Payload Cleanup Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Eliminate newly-created orphan replay payload files on manifest-save failure and lazily clean up historical orphan payloads during replay runtime startup.

**Architecture:** Keep the current `payload -> manifest` visibility contract intact. Add a small rollback seam to `CombatReplayPersistenceQueue` so manifest-save failures delete the just-written payload, and add a startup-time orphan scan in `CombatReplayRuntime` that cross-checks payload battle ids against the manifest catalog before accepting new replay work.

**Tech Stack:** C#, Unity MonoBehaviours, SQLite, filesystem-backed replay payload storage, custom executable source-reading tests

---

## Task 1: Add the failing replay-hardening assertions

**Files:**
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`

**Step 1: Write the failing test**

Add source assertions that require:

- `CombatReplayPayloadStore` to expose `Delete(string battleId)` and `ListBattleIds()`,
- `CombatReplayPersistenceQueue` to roll back the payload file when manifest persistence fails after payload persistence succeeds,
- and `CombatReplayRuntime.Awake()` to invoke an orphan-payload cleanup path that uses both the payload store and battle catalog.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: FAIL because the payload store cannot enumerate/delete payloads yet, the queue does not perform rollback deletion, and runtime startup does not clean historical orphan payloads.

**Step 3: Write minimal implementation**

Do not implement yet in this task.

**Step 4: Run test to verify it still fails for the intended reason**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: FAIL at the new orphan-cleanup assertions.

## Task 2: Implement rollback + lazy orphan cleanup

**Files:**
- Modify: `Game/CombatReplay/CombatReplayPayloadStore.cs`
- Modify: `Game/CombatReplay/CombatReplayPersistenceQueue.cs`
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`

**Step 1: Write the failing test**

Reuse the Task 1 assertions; do not add new ones until they are green.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: FAIL

**Step 3: Write minimal implementation**

Implement the smallest viable hardening:

- in `CombatReplayPayloadStore`, add `Delete(...)` and `ListBattleIds()` based on the existing `*.payload.json` naming convention,
- in `CombatReplayPersistenceQueue`, inject a payload-delete callback and invoke it only when manifest persistence fails after payload persistence succeeded,
- in `CombatReplayRuntime.Awake()`, add a best-effort `CleanupOrphanedPayloads()` pass after `_battleCatalog` and `_payloadStore` are initialized,
- and keep all cleanup failures observable via logging without blocking runtime startup.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: PASS

## Task 3: Focused verification

**Files:**
- Test: `tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

**Step 1: Run focused verification**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: PASS

**Step 2: Record residual risk**

If the implementation intentionally leaves them out, call out:

- that this pass only removes payload orphans, not manifest-visible entries whose payload file is missing,
- and that startup-time orphan cleanup is a best-effort sweep rather than a transactional persistence guarantee.

**Step 3: Optional follow-up note**

If further hardening is desired later, list temp-file / two-phase payload promotion as the next design step rather than extending this patch ad hoc.
