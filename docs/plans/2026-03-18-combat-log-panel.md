# Combat Log Panel Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a runtime-only combat log panel beside the debug panel that derives rows from raw `CombatSim` frames and advances row visibility using processed combat frame progress.

**Architecture:** Introduce a small runtime pipeline that listens for `CombatSimReceived`, builds an in-memory timeline and display-row list for the current combat, and renders those rows in a dedicated side panel. Playback synchronization comes from the existing processed-combat-frame count, interpreted as a count rather than a frame index, so the panel highlights the last processed row correctly under speed changes, pause, and final-blow slowdown. The panel supports independent visibility toggling and distinct future-row behavior for first play versus explicit replay playback.

**Tech Stack:** C# 12, BepInEx, Harmony, Unity MonoBehaviours, existing debug-panel runtime UI helpers, console-style source/state tests.

---

### Task 1: Add failing tests for the combat log runtime model

**Files:**
- Modify: `bazaarplusplus-mod/tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`
- Modify: `bazaarplusplus-mod/tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj` if additional source references are required

**Step 1: Write the failing tests**

Add focused tests that assert:

- a combat-log playback pass enum exists with first-play and replay values
- a row visual-state mapping exists for played, current, hidden-future, and dimmed-future
- playback state maps row frame indices correctly from processed-frame count
- `ProcessedCombatFrames == 0` yields no current row
- `ProcessedCombatFrames == 1` highlights frame `0`

Example assertions:

```csharp
Assert.Equal(-1, CombatLogPlaybackState.GetLastProcessedFrameIndex(0));
Assert.Equal(0, CombatLogPlaybackState.GetLastProcessedFrameIndex(1));
Assert.Equal(CombatLogRowVisualState.FutureHidden, CombatLogPlaybackState.GetVisualState(12, 10, CombatLogPlaybackPass.FirstPlay));
Assert.Equal(CombatLogRowVisualState.FutureDimmed, CombatLogPlaybackState.GetVisualState(12, 10, CombatLogPlaybackPass.Replay));
Assert.Equal(CombatLogRowVisualState.Current, CombatLogPlaybackState.GetVisualState(0, 1, CombatLogPlaybackPass.FirstPlay));
```

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: FAIL because the combat log playback types do not exist yet.

**Step 3: Write minimal implementation**

Create the smallest combat-log playback state types and helpers needed to satisfy the tests.

**Step 4: Run tests to verify they pass**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/tests/CombatStatusBarState.Tests bazaarplusplus-mod/Game/CombatLog
git commit -m "feat: add combat log playback state model"
```

### Task 2: Add failing tests for timeline and row formatting

**Files:**
- Create: `bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`
- Create: `bazaarplusplus-mod/tests/CombatLogRuntime.Tests/Program.cs`
- Create: `bazaarplusplus-mod/Game/CombatLog/CombatLogRuntime.cs`
- Create: `bazaarplusplus-mod/Game/CombatLog/CombatLogFormatter.cs`
- Create: `bazaarplusplus-mod/Game/CombatLog/CombatLogModels.cs`

**Step 1: Write the failing tests**

Add tests that build small synthetic combat timelines and assert:

- one `CombatSimFrame` becomes one `CombatLogFrame`
- `LogicalTime` uses `frameIndex * 50ms`
- supported event and update types produce deterministic display rows
- row ordering stays stable inside a frame

Start with:

- `EffectExecuted`
- player health adjustment
- player attribute update
- card attribute update
- `CombatantDied`

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL because the runtime and formatter types do not exist.

**Step 3: Write minimal implementation**

Create:

- timeline models
- display row models
- a runtime builder that maps `CombatSim` to timeline frames
- a formatter that maps supported frame contents to display rows

Keep unsupported event types as no-op or explicit unknown placeholders.

**Step 4: Run tests to verify they pass**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/tests/CombatLogRuntime.Tests bazaarplusplus-mod/Game/CombatLog
git commit -m "feat: build combat log timeline and formatter"
```

### Task 3: Add failing tests for explicit replay-pass detection and timeline replacement

**Files:**
- Modify: `bazaarplusplus-mod/tests/CombatLogRuntime.Tests/Program.cs`
- Modify: `bazaarplusplus-mod/Game/CombatLog/CombatLogRuntime.cs`

**Step 1: Write the failing tests**

Add tests that assert:

- a newly received live combat timeline is marked `FirstPlay`
- replay mode is entered only when an explicit replay-source flag or replay controller state is present
- a different live combat replaces the old timeline and stays `FirstPlay`
- content similarity alone does not switch a live combat into `Replay`

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL because explicit replay-pass detection is not implemented yet.

**Step 3: Write minimal implementation**

Add replay-source-aware playback-pass transition logic to the runtime. Prefer an explicit flag or runtime query from the combat replay feature rather than content hashing.

**Step 4: Run tests to verify they pass**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/tests/CombatLogRuntime.Tests/Program.cs bazaarplusplus-mod/Game/CombatLog/CombatLogRuntime.cs
git commit -m "feat: detect explicit combat log replay pass"
```

### Task 4: Attach the runtime controller to the plugin

**Files:**
- Modify: `bazaarplusplus-mod/Plugin.cs`
- Create: `bazaarplusplus-mod/Game/CombatLog/CombatLogController.cs`
- Modify: `bazaarplusplus-mod/BazaarPlusPlus.csproj` if needed

**Step 1: Write the failing test**

Add assertions that source files contain:

- a combat-log runtime controller type
- a plugin attach path for the controller
- a listener or subscription path from combat events into the combat-log runtime

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL because the plugin has not attached combat-log runtime components yet.

**Step 3: Write minimal implementation**

Attach the combat-log controller from `Plugin.Awake()` and ensure it owns the runtime lifecycle for the current session.

**Step 4: Run tests to verify they pass**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/Plugin.cs bazaarplusplus-mod/Game/CombatLog
git commit -m "feat: attach combat log runtime controller"
```

### Task 5: Add failing tests for panel state and frame-follow behavior

**Files:**
- Modify: `bazaarplusplus-mod/tests/CombatLogRuntime.Tests/Program.cs`
- Create: `bazaarplusplus-mod/Game/CombatLog/CombatLogPanel.cs`
- Create: `bazaarplusplus-mod/Game/CombatLog/CombatLogPanelState.cs`

**Step 1: Write the failing tests**

Add tests that assert:

- the panel reads processed combat frame progress rather than elapsed wall-clock time
- the row-state mapping yields hidden future rows on first play
- the row-state mapping yields dimmed future rows on replay
- the current row updates when processed frame progress changes
- the panel highlights the last processed frame instead of the next frame

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL because the panel state and frame-follow logic do not exist yet.

**Step 3: Write minimal implementation**

Implement panel-state helpers first:

- current frame getter
- last-processed-frame calculation
- row visual-state mapping
- current-row selection
- filtered row list for first-play mode

Do not build full UI until the state logic is proven.

**Step 4: Run tests to verify they pass**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/tests/CombatLogRuntime.Tests/Program.cs bazaarplusplus-mod/Game/CombatLog
git commit -m "feat: add combat log panel playback state"
```

### Task 6: Render the combat log panel beside the debug panel with an independent toggle

**Files:**
- Modify: `bazaarplusplus-mod/Game/DebugPanel/DebugPanel.cs`
- Modify: `bazaarplusplus-mod/Game/DebugPanel/DebugPanelState.cs`
- Modify: `bazaarplusplus-mod/Game/Input/KeyBindings.cs`
- Modify: `bazaarplusplus-mod/Game/CombatLog/CombatLogPanel.cs`
- Modify: `bazaarplusplus-mod/tests/CombatLogRuntime.Tests/Program.cs`

**Step 1: Write the failing test**

Add assertions that:

- the combat log panel has an independent toggle path
- the debug UI uses a second IMGUI window or explicit side-by-side area for the combat log
- the combat log panel reads rows from the combat-log runtime
- the combat log is not implemented as another `DebugPanelSection`

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: FAIL because the UI has not been wired into the debug panel yet.

**Step 3: Write minimal implementation**

Extend the debug panel layout so a combat-log panel can appear beside it. Keep the first version narrow:

- scrollable text rows
- current row highlight
- replay dimming
- first-play hiding
- independent visibility toggle

Implement the layout as a true side panel:

- a second IMGUI window rect anchored beside the debug panel, or
- an explicit top-level split layout that renders both panels simultaneously

Avoid adding filtering, search, or extra sections.

**Step 4: Run tests to verify they pass**

Run: `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`

Expected: PASS

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/Game/DebugPanel bazaarplusplus-mod/Game/Input/KeyBindings.cs bazaarplusplus-mod/Game/CombatLog bazaarplusplus-mod/tests/CombatLogRuntime.Tests/Program.cs
git commit -m "feat: add combat log panel beside debug panel"
```

### Task 7: Run verification and update docs

**Files:**
- Modify: `bazaarplusplus-mod/README.md` if debug usage needs a short mention
- Modify: `bazaarplusplus-mod/docs/reference/combat-replay-recording.md` only if cross-reference is useful
- Modify: `bazaarplusplus-mod/docs/combat-status-bar.md` only if shared processed-frame assumptions need a note

**Step 1: Document behavior**

Write concise docs covering:

- the combat log panel is runtime-only
- it uses combat frame progress rather than real time
- future rows are hidden on first play and dimmed during explicit replay playback
- it lives beside the debug panel and has an independent toggle

**Step 2: Run focused tests**

Run:

- `dotnet run --project bazaarplusplus-mod/tests/CombatLogRuntime.Tests/CombatLogRuntime.Tests.csproj`
- `dotnet run --project bazaarplusplus-mod/tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Expected: PASS

**Step 3: Run broader regression checks**

Run:

- `dotnet run --project bazaarplusplus-mod/tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet run --project bazaarplusplus-mod/tests/EncounterTrackerState.Tests/EncounterTrackerState.Tests.csproj`

Expected: PASS

**Step 4: Build the plugin**

Run: `dotnet build bazaarplusplus-mod/BazaarPlusPlus.csproj`

Expected: BUILD SUCCEEDED

**Step 5: Commit**

```bash
git add bazaarplusplus-mod/README.md bazaarplusplus-mod/docs bazaarplusplus-mod/tests
git commit -m "docs: add combat log panel runtime notes"
```
