# Offline Replay Bootstrap Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Allow saved combat replays to start from the lobby by bootstrapping only the minimum native replay environment, without calling `RunManager.StartRun()`.

**Architecture:** Refactor `CombatReplayRuntime` so the lobby replay path is split into scene preparation, replay dependency resolution, saved sequence injection, and rollback handling. Keep using the native `ReplayState` pipeline, but remove any dependency on normal run lifecycle entrypoints.

**Tech Stack:** C# 12, Unity MonoBehaviours, Harmony, reflection against The Bazaar runtime, existing console-style source-assertion tests.

---

### Task 1: Lock the bootstrap contract in tests

**Files:**
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`
- Test: `tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

**Step 1: Write the failing test**

Add source assertions that require:

- `Game/CombatReplay/CombatReplayRuntime.cs` contains `EnsureReplayBootstrapReadyAsync`
- `Game/CombatReplay/CombatReplayRuntime.cs` contains `ResolveReplayDependencies`
- `Game/CombatReplay/CombatReplayRuntime.cs` contains `RollbackReplayBootstrapAsync`
- `Game/CombatReplay/CombatReplayRuntime.cs` does not contain `StartRun()`
- `Game/CombatReplay/CombatReplayRuntime.cs` does not contain `Events.RunStarted.Trigger()`

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: FAIL because the current runtime still contains `StartRun()` and does not contain the new bootstrap structure.

**Step 3: Commit**

```bash
git add tests/CombatReplayRecording.Tests/Program.cs
git commit -m "test: lock offline replay bootstrap contract"
```

### Task 2: Introduce replay bootstrap dependency resolution

**Files:**
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`

**Step 1: Write the failing test**

Extend the source assertions to require a small internal dependency resolution layer in `CombatReplayRuntime.cs`:

- a `ReplayBootstrapContext` type exists
- `ResolveReplayDependencies()` returns or populates that context
- the context covers processor access and replay trigger access

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: FAIL because the runtime still resolves reflection dependencies ad hoc.

**Step 3: Write minimal implementation**

Refactor `Game/CombatReplay/CombatReplayRuntime.cs` to:

- add `ReplayBootstrapContext`
- move reflection lookups for `SocketBehavior`, `NetMessageProcessor`, `GameSimHandler`, `LastCombatSequence`, and `CombatSequenceCreated` under `ResolveReplayDependencies()`
- keep behavior unchanged for now except for the new structure

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add Game/CombatReplay/CombatReplayRuntime.cs tests/CombatReplayRecording.Tests/Program.cs
git commit -m "refactor: isolate replay bootstrap dependencies"
```

### Task 3: Replace run-start bootstrap with scene-only bootstrap

**Files:**
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`

**Step 1: Write the failing test**

Add or tighten assertions so the runtime source requires:

- `EnsureReplayBootstrapReadyAsync()` to load scenes and wait for managers
- no `RunManager.StartRun()`
- no `Events.RunStarted.Trigger()`

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: FAIL because the current implementation still uses run-start bootstrap.

**Step 3: Write minimal implementation**

Update `Game/CombatReplay/CombatReplayRuntime.cs` to:

- remove `RunManager.StartRun()` from the saved replay bootstrap path
- remove `Events.RunStarted.Trigger()` from the saved replay bootstrap path
- define replay readiness using scene load plus initialized `BoardManager` and `GameServiceManager`
- keep `SceneID.GameScene` and `SceneID.GameplayLoading` orchestration only if still needed

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: PASS

**Step 5: Build to verify compile safety**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: BUILD SUCCEEDED

**Step 6: Commit**

```bash
git add Game/CombatReplay/CombatReplayRuntime.cs tests/CombatReplayRecording.Tests/Program.cs
git commit -m "feat: bootstrap saved replays without run start"
```

### Task 4: Separate replay injection from bootstrap

**Files:**
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`

**Step 1: Write the failing test**

Add source assertions that require:

- a dedicated replay injection method such as `TryInjectSavedReplayAsync`
- `HandleSpawnMessageAsync(...)` appears before `TriggerCombatSequenceCreated(...)`
- `AppState.TryPushState<ReplayState>()` still happens after sequence injection

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: FAIL because injection is still embedded inside the runtime start flow.

**Step 3: Write minimal implementation**

Refactor the runtime so replay injection is executed by a dedicated method that:

- sets `LastCombatSequence`
- handles the opening `GameSim`
- triggers `CombatSequenceCreated`
- pushes `ReplayState`

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: PASS

**Step 5: Build to verify compile safety**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: BUILD SUCCEEDED

**Step 6: Commit**

```bash
git add Game/CombatReplay/CombatReplayRuntime.cs tests/CombatReplayRecording.Tests/Program.cs
git commit -m "refactor: split saved replay injection flow"
```

### Task 5: Add explicit logging and rollback checkpoints

**Files:**
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Modify: `tests/CombatReplayRecording.Tests/Program.cs`

**Step 1: Write the failing test**

Add source assertions that require logging markers for:

- bootstrap start
- scene readiness completion
- dependency resolution completion
- replay start success
- rollback failure handling

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: FAIL because the required logging coverage is not yet complete.

**Step 3: Write minimal implementation**

Add structured `BppLog` messages around:

- entering bootstrap
- finishing scene preparation
- resolving replay dependencies
- injecting the saved sequence
- entering `ReplayState`
- rollback and menu-return paths

Keep rollback consolidated in `RollbackReplayBootstrapAsync()`.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: PASS

**Step 5: Build to verify compile safety**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: BUILD SUCCEEDED

**Step 6: Commit**

```bash
git add Game/CombatReplay/CombatReplayRuntime.cs tests/CombatReplayRecording.Tests/Program.cs
git commit -m "chore: add replay bootstrap diagnostics"
```

### Task 6: Update docs for offline replay bootstrap

**Files:**
- Modify: `docs/reference/combat-replay-recording.md`
- Modify: `README.md`

**Step 1: Document the new behavior**

Update the docs so they clearly state:

- saved replay bootstrap no longer uses a normal run start
- saved replay playback still requires the lobby with no active run
- verification is based on local logs and code-path review

**Step 2: Run focused verification**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: PASS

**Step 3: Run build verification**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: BUILD SUCCEEDED

**Step 4: Commit**

```bash
git add docs/reference/combat-replay-recording.md README.md
git commit -m "docs: describe offline replay bootstrap"
```

### Task 7: Final verification

**Files:**
- Test: `tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- Test: `BazaarPlusPlus.csproj`

**Step 1: Run replay contract test**

Run: `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`

Expected: PASS

**Step 2: Run build**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: BUILD SUCCEEDED

**Step 3: Record manual runtime checklist**

Verify from local logs during one saved replay launch that:

- bootstrap starts from the lobby
- no run-start entrypoint is logged
- replay dependencies resolve
- replay enters `ReplayState`
- exiting replay returns to the main menu

**Step 4: Commit final runtime changes if needed**

```bash
git status --short
```
