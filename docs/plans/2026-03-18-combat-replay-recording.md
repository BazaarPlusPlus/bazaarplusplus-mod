# Combat Replay Recording Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Record each combat's `GameSim -> CombatSim -> GameSim` message triplet and allow replaying saved combats from the in-game debug panel after restarting the game.

**Architecture:** Hook the raw network message stream at `NetMessageProcessor.ReceiveOrQueue` to capture combat sequence candidates before runtime state mutation. Persist completed combat triplets as local JSON files with MessagePack payloads encoded as base64, then expose a small runtime service that loads a saved triplet, injects it as the current replay source, and enters the game's existing `ReplayState` pipeline.

**Tech Stack:** C# 12, Harmony patches, BepInEx/Unity MonoBehaviours, Newtonsoft.Json, existing Bazaar runtime message types.

---

### Task 1: Add a failing persistence-and-selection test

**Files:**
- Create: `bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- Create: `bazaarplusplus-mod/tests/CombatReplayRecording.Tests/Program.cs`

**Step 1: Write the failing test**

Write a focused console test that loads the built plugin assembly and asserts:
- a `CombatReplayStore` type exists
- a `CombatReplayRecord` model exists
- the store can save a record, list it newest-first, and reload it by id

**Step 2: Run test to verify it fails**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: FAIL because the replay types do not exist yet.

**Step 3: Write minimal implementation**

Create the replay record model and file-backed store with only the API surface needed by the test.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/tests/CombatReplayRecording.Tests bazaarplusplus-mod/Game/CombatReplay
git commit -m "feat: add combat replay persistence store"
```

### Task 2: Add a failing capture service test

**Files:**
- Modify: `bazaarplusplus-mod/tests/CombatReplayRecording.Tests/Program.cs`
- Create: `bazaarplusplus-mod/Game/CombatReplay/CombatReplayCaptureService.cs`
- Create: `bazaarplusplus-mod/Game/CombatReplay/CombatReplaySequenceCandidate.cs`

**Step 1: Write the failing test**

Add a test that feeds message type names into a capture service and asserts:
- only a `GameSim, CombatSim, GameSim` pattern completes a replay record
- non-combat triplets are ignored
- metadata from the first/last `GameSim` is preserved on the completed record

**Step 2: Run test to verify it fails**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: FAIL because the capture service does not exist.

**Step 3: Write minimal implementation**

Implement a small sequence assembler that accepts raw messages, tracks the rolling triplet, validates combat state on the opening `GameSim`, and emits a completed record payload.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/tests/CombatReplayRecording.Tests/Program.cs bazaarplusplus-mod/Game/CombatReplay
git commit -m "feat: assemble combat replay sequences"
```

### Task 3: Add a failing loader/controller test

**Files:**
- Modify: `bazaarplusplus-mod/tests/CombatReplayRecording.Tests/Program.cs`
- Create: `bazaarplusplus-mod/Game/CombatReplay/CombatReplayLoader.cs`
- Create: `bazaarplusplus-mod/Game/CombatReplay/CombatReplayController.cs`

**Step 1: Write the failing test**

Add a test that asserts:
- the loader can reconstruct a saved record into a runtime replay payload
- the controller tracks the active saved replay id
- the controller exposes latest saved records for the debug panel

**Step 2: Run test to verify it fails**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: FAIL because loader/controller types do not exist.

**Step 3: Write minimal implementation**

Implement a runtime controller that wraps the store and capture service, plus a loader that converts persisted payloads back to replayable runtime objects.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/tests/CombatReplayRecording.Tests/Program.cs bazaarplusplus-mod/Game/CombatReplay
git commit -m "feat: add combat replay runtime controller"
```

### Task 4: Hook capture into the game

**Files:**
- Modify: `bazaarplusplus-mod/Plugin.cs`
- Modify: `bazaarplusplus-mod/Models/ModState.cs`
- Create: `bazaarplusplus-mod/Patches/Combat/CombatReplayCapturePatch.cs`
- Modify: `bazaarplusplus-mod/BazaarPlusPlus.csproj` if additional references are needed

**Step 1: Write the failing test**

Add assertions that source files contain:
- a Harmony patch on `NetMessageProcessor.ReceiveOrQueue`
- a call path that forwards incoming messages into the replay controller
- a configured replay storage path in `ModState`

**Step 2: Run test to verify it fails**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: FAIL because the patch and config path are not present.

**Step 3: Write minimal implementation**

Add the replay storage path to `ModState`, create the capture patch, and attach the replay controller from `Plugin`.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/Plugin.cs bazaarplusplus-mod/Models/ModState.cs bazaarplusplus-mod/Patches/Combat/CombatReplayCapturePatch.cs bazaarplusplus-mod/tests/CombatReplayRecording.Tests/Program.cs
git commit -m "feat: capture combat replays from live messages"
```

### Task 5: Hook replay loading into the debug panel

**Files:**
- Modify: `bazaarplusplus-mod/Game/DebugPanel/DebugPanel.cs`
- Modify: `bazaarplusplus-mod/Game/DebugPanel/DebugPanelState.cs`
- Modify: `bazaarplusplus-mod/tests/CombatReplayRecording.Tests/Program.cs`

**Step 1: Write the failing test**

Add assertions that:
- `DebugPanelSection` has a combat replay section
- debug panel source includes controls for replaying the latest combat and saved entries
- the panel reads replay data from the replay controller

**Step 2: Run test to verify it fails**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: FAIL because the debug panel does not expose replay UI yet.

**Step 3: Write minimal implementation**

Extend the debug panel with a new section that lists recent records, a button for replaying the newest record, and a button per saved record. Keep UI minimal and debug-only.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/Game/DebugPanel bazaarplusplus-mod/tests/CombatReplayRecording.Tests/Program.cs
git commit -m "feat: add combat replay controls to debug panel"
```

### Task 6: Add docs and run full verification

**Files:**
- Modify: `bazaarplusplus-mod/README.md`
- Create or Modify: `bazaarplusplus-mod/docs/reference/combat-replay-recording.md`

**Step 1: Document behavior**

Write concise docs covering:
- where replay files are stored
- what is recorded
- how to start replay from the debug panel
- limitations of saved combat replay

**Step 2: Run focused tests**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
Expected: PASS

**Step 3: Run broader regression checks**

Run:
- `dotnet run --project bazaarplusplus-mod/tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
- `dotnet run --project bazaarplusplus-mod/tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj`

Expected: PASS

**Step 4: Build the plugin**

Run: `dotnet build bazaarplusplus-mod/BazaarPlusPlus.csproj`
Expected: BUILD SUCCEEDED

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/README.md bazaarplusplus-mod/docs/reference/combat-replay-recording.md
git commit -m "docs: document combat replay recording"
```
