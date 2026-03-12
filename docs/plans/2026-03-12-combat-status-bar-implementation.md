# Combat Status Bar Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor combat playback state ownership into `CombatStatusBar` and rebuild the UI as a four-segment capsule controller.

**Architecture:** `CombatStatusBar` becomes the single owner of combat playback overlay state and exposes a narrow static API for combat patches. Harmony patches and GUI rendering stop depending on combat fields stored in `ModState`. The IMGUI overlay moves from an auto-layout debug strip to fixed segment rects.

**Tech Stack:** C# 12, Unity IMGUI, Harmony, xUnit

---

### Task 1: Add combat controller regression tests

**Files:**
- Modify: `tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`
- Create: `tests/BazaarPlusPlus.Tests/CombatStatusBarStateTests.cs`
- Test: `tests/BazaarPlusPlus.Tests/CombatStatusBarStateTests.cs`

**Step 1: Write the failing test**

Add tests that cover:

- starting combat resets processed frame count and enables playback
- setting frame total resets processed count and clamps total to zero or above
- advancing frames stops at total frame count
- speed stepping moves across discrete speed steps and clamps at the edges
- logical elapsed time is derived from processed frame count

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: FAIL because the combat controller code is not linked into the test project yet or does not expose the required API.

**Step 3: Write minimal implementation**

Link the combat controller source into the test project and add only the minimal public/internal API required for the tests.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: PASS

### Task 2: Move combat playback state out of ModState

**Files:**
- Modify: `Models/ModState.cs`
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`
- Modify: `Patches/Combat/CombatSpeedPatch.cs`
- Modify: `Patches/Combat/CombatSimulationPatches.cs`

**Step 1: Write the failing test**

Extend tests to verify the combat controller API can be driven without `ModState` owning combat playback fields.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: FAIL until combat ownership is moved.

**Step 3: Write minimal implementation**

- remove combat playback runtime fields and helpers from `ModState`
- keep only combat config entries there
- add a static combat controller API on `CombatStatusBar`
- update Harmony patches and event handlers to call `CombatStatusBar`

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: PASS

### Task 3: Rebuild the overlay layout

**Files:**
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`
- Modify: `docs/combat-status-bar.md`

**Step 1: Write the failing test**

If practical, add a small pure-state formatting test for speed labels or frame formatting. Keep rendering itself untested if Unity IMGUI makes that disproportionate.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: FAIL only if new pure helpers were added to support the capsule controller behavior.

**Step 3: Write minimal implementation**

- replace `GUILayout` toolbar layout with fixed segment rect layout
- draw four segments: `Time`, `Frame`, `Multiplier`, `Pause`
- wire multiplier segment to step previous/next behavior
- render pause segment disabled

**Step 4: Run tests and build**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Run: `dotnet build tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Expected: PASS

### Task 4: Refresh docs

**Files:**
- Modify: `docs/combat-status-bar.md`

**Step 1: Update documentation**

Refresh the runtime model and UI section so it reflects:

- `CombatStatusBar` as combat-state owner
- four-segment capsule layout
- step-based multiplier controls
- disabled pause segment

**Step 2: Run final verification**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Run: `dotnet build tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Expected: PASS
