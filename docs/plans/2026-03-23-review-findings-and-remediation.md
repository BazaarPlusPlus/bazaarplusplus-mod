# Review Findings And Remediation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Close the confirmed correctness regressions in the current uncommitted patch without undoing the performance improvements it introduced.

**Architecture:** Preserve the async replay persistence and lazy UI/history optimizations, but restore correctness through two focused changes. First, canonicalize mouse bindings around a single `<Mouse>/<controlName>` representation that is used consistently for storage, validation, rebinding, and runtime hold detection. Second, keep replay persistence asynchronous while adding explicit queue lifecycle semantics and a deferred run-completion path so `pvp_combat_recorded` can still be recorded before the active run session is torn down.

**Tech Stack:** C#, Unity MonoBehaviours, Unity Input System, SQLite, custom executable tests, source-reading tests

---

## Confirmed Findings

### P1: Mouse hotkeys are not represented consistently end-to-end

- `Game/Input/BppHotkeyService.cs` stores mouse bindings in `<Mouse>/...` syntax but resolves live controls by comparing against `InputControl.path`.
- `Game/Input/BppKeyBindRowController.cs` captures `buttonControl.path` during rebinding even though `NormalizeBindingPath(...)` only accepts `<Mouse>/...`.
- Net effect: mouse-backed preview hotkeys are not trustworthy at runtime and may not save correctly from the native settings UI.

### P2: Replay recording is now timing-sensitive at run shutdown

- `Game/CombatReplay/CombatReplayRuntime.cs` publishes `PvpBattleRecorded` only after background persistence completes.
- `Game/RunLogging/RunLoggingModule.cs` still requires `BppRuntimeHost.RunContext.IsInGameRun == true` before appending the replay event.
- Net effect: the last PVP combat in a run can miss `pvp_combat_recorded` if replay persistence completes after the run-exit sync path has already completed the run.

### P2: Replay queue disposal can silently drop pending work

- `Game/CombatReplay/CombatReplayPersistenceQueue.cs` currently cancels the worker immediately during `Dispose()`.
- Pending or just-enqueued replay writes can be abandoned during teardown, and the loss is silent apart from missing downstream events.
- The real issue is dropped work and incomplete shutdown semantics, not a broad “resource leak” or a guaranteed use-after-free.

## Issues Explicitly Not Treated As Patch Blockers

### Existing design debt: payload write before manifest write

- Writing the payload file before the sqlite manifest can leave an orphaned payload if the second step fails.
- This is a real persistence-model limitation, but it already existed in the synchronous implementation and is not a regression introduced by this patch.
- Keep it as a follow-up hardening item, not as a blocker for the current review.

### Tooltip hide-before-check ordering

- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs` hides the primary tooltip before the final revalidation block.
- This may be worth tightening later, but the current evidence points to possible flicker or UX roughness rather than a clear, durable correctness break.

### `Mouse.current == null` validation nuance

- `NormalizeBindingPath(...)` already rejects `scroll`, `position`, and `delta` paths before any live-device lookup.
- The remaining concern is only that validation of uncommon mouse controls becomes weaker when no live mouse device is available.
- That is narrower than the original review comment and does not justify a blocker by itself.

## Recommended Remediation Strategy

### Recommendation 1: Canonicalize mouse bindings around control names

Use a single canonical representation for mouse bindings: `<Mouse>/<buttonName>`.

- Rebinding should save `<Mouse>/{buttonControl.name}` instead of `buttonControl.path`.
- Runtime hold detection should parse the canonical binding and resolve live buttons by control name rather than by raw `InputControl.path`.
- Validation should continue to reject unsupported continuous mouse controls and synthetic buttons.

This keeps the fix local and avoids depending on Unity’s effective control-path formatting in multiple codepaths.

### Recommendation 2: Keep `PvpBattleRecorded` post-persistence, but delay run completion while replay writes drain

Do not revert to publishing `PvpBattleRecorded` before persistence succeeds. That would weaken the contract between the event bus and durable storage.

Instead:

- track whether replay persistence is still pending,
- expose that state through `CombatReplayRuntime`,
- and defer `_core.CompleteRun(...)` in `RunLoggingModule` while replay persistence is still outstanding.

Use a short bounded grace window so run completion is not blocked indefinitely if persistence stalls or fails.

### Recommendation 3: Replace cancel-first disposal with graceful stop semantics

`Dispose()` should not immediately cancel the worker and abandon `_pending`.

Recommended behavior:

- stop accepting new enqueue requests,
- let the worker drain queued work and in-flight saves,
- wait only briefly during teardown,
- then dispose owned synchronization primitives,
- and log if any work had to be abandoned after timeout.

This preserves the performance win from async persistence while making shutdown behavior explicit and observable.

## Task 1: Canonicalize mouse binding representation

**Files:**
- Modify: `Game/Input/BppHotkeyService.cs`
- Modify: `Game/Input/BppKeyBindRowController.cs`
- Modify: `tests/ItemEnchantPreview.Tests/Program.cs`

**Step 1: Write the failing test**

Add source assertions that require:

- rebinding to build mouse binding paths from `buttonControl.name`,
- runtime lookup to resolve mouse bindings from the canonical `<Mouse>/...` path rather than by comparing against raw `InputControl.path`,
- and normalization to keep rejecting unsupported mouse controls.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`
Expected: FAIL before implementation because the current code still persists and resolves mouse controls via incompatible path formats.

**Step 3: Write minimal implementation**

Implement a single canonical mouse-binding flow:

- parse/store mouse bindings as `<Mouse>/<buttonName>`,
- build rebinding results from `buttonControl.name`,
- resolve held mouse buttons by control name,
- keep alias/display handling for left/right/middle/back/forward buttons,
- and preserve rejection of unsupported continuous controls.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`
Expected: PASS

## Task 2: Defer run completion while replay persistence is still outstanding

**Files:**
- Modify: `Game/CombatReplay/CombatReplayPersistenceQueue.cs`
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Modify: `Game/RunLogging/RunLoggingModule.cs`
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`
- Modify: `tests/RunLoggingSession.Tests/Program.cs`

**Step 1: Write the failing tests**

Add focused assertions that require:

- the replay queue/runtime to expose whether persistence work is still outstanding,
- `RunLoggingModule` to defer run completion when leaving a run while replay persistence is still pending,
- and final replay events to remain publish-after-persist rather than reverting to optimistic publication.

**Step 2: Run tests to verify they fail**

Run:
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`

Expected: FAIL before implementation because run completion is currently independent from replay persistence state.

**Step 3: Write minimal implementation**

Implement a deferred-completion path:

- track outstanding replay persistence count in the queue/runtime,
- expose a `HasPendingPersistence`-style seam from `CombatReplayRuntime`,
- when the run exits, hold off on `_core.CompleteRun(...)` while replay persistence is still outstanding,
- complete the run once the queue drains or a short grace timeout expires,
- and keep `PvpBattleRecorded` emission tied to successful persistence completion.

**Step 4: Run tests to verify they pass**

Run:
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`

Expected: PASS

## Task 3: Make replay queue shutdown explicit and graceful

**Files:**
- Modify: `Game/CombatReplay/CombatReplayPersistenceQueue.cs`
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`

**Step 1: Write the failing test**

Add source assertions that require:

- `Dispose()` to stop new enqueues without immediately abandoning queued work,
- the worker loop to exit only after a stop request and an empty/outstanding-free queue state,
- and teardown to log or otherwise surface abandoned work if a bounded wait expires.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: FAIL before implementation because shutdown is currently cancel-first and silent.

**Step 3: Write minimal implementation**

Refactor shutdown semantics:

- replace immediate cancellation as the primary stop mechanism with an explicit “stop accepting work” state,
- let the worker drain pending and in-flight requests before exiting,
- use only a short bounded wait during teardown rather than a multi-second blocking wait on the Unity main thread,
- dispose `_signal` and `_shutdown` after the worker exits or the timeout path is reached,
- and log pending count when the timeout path forces abandonment.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: PASS

## Task 4: Focused verification and residual-risk review

**Files:**
- Test: `tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`
- Test: `tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- Test: `tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`
- Read: `docs/reference/upgrade-tooltip-implementation.md`

**Step 1: Run focused verification**

Run:
- `dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`
- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`

Expected: PASS

**Step 2: Record residual risk**

Call out two remaining follow-ups if they are intentionally left out of this patch:

- payload/manifest partial-failure cleanup strategy,
- and whether the upgrade-tooltip refresh path should be tightened to avoid any transient hide/re-show flicker.

**Step 3: Optional environment-dependent verification**

If the local Unity test harness is healthy, also run:

- `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

If it still fails because the executable harness cannot load Unity runtime assemblies in the current environment, document that as a pre-existing test-environment limitation rather than as a regression from this patch.
