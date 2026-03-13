# Combat Status Bar Canvas Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the IMGUI `CombatStatusBar` with a runtime `Canvas` HUD that preserves the four-segment capsule layout, improves resolution consistency, and adds light UI polish.

**Architecture:** `CombatStatusBar` keeps owning combat playback state and display helpers. The implementation swaps the `OnGUI()` renderer for a runtime-built uGUI tree backed by cached `RectTransform`, `Image`, `Text`, and `Button` references. A dedicated overlay canvas with a `CanvasScaler` handles layout stability across resolutions.

**Tech Stack:** C# 12, Unity uGUI (`Canvas`, `CanvasScaler`, `Button`, `Image`, `Text`), Harmony, xUnit

---

### Task 1: Freeze the display/state contract with tests

**Files:**
- Inspect: `Game/CombatStatusBar/CombatStatusBar.State.cs`
- Inspect: `Game/CombatStatusBar/CombatStatusBar.cs`
- Modify: `tests/BazaarPlusPlus.Tests/CombatStatusBarStateTests.cs`
- Test: `tests/BazaarPlusPlus.Tests/CombatStatusBarStateTests.cs`

**Step 1: Write the failing test**

Add or extend tests to cover the display contract that the Canvas UI will consume:

- `GetDisplayedTimeText()` returns standby text out of combat and formatted logical time in combat
- `GetDisplayedFrameText()` returns `Standby` out of combat and processed frame count in combat
- `FormatCombatSpeedLabel()` formats current speed consistently
- `AdvanceVisualBlend()` approaches `0` or `1` without overshooting
- pause enablement is gated by combat state and game service availability where practical

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: FAIL only for newly added coverage.

**Step 3: Write minimal implementation**

Add only the pure helper adjustments needed to make the display contract explicit and testable. Do not start the Canvas migration yet.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: PASS

**Step 5: Commit**

```bash
git add tests/BazaarPlusPlus.Tests/CombatStatusBarStateTests.cs Game/CombatStatusBar/CombatStatusBar.State.cs
git commit -m "test: lock combat status bar display contract"
```

### Task 2: Introduce a runtime Canvas shell

**Files:**
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`
- Inspect: `BazaarPlusPlus.csproj`

**Step 1: Write the failing test**

No new automated rendering test is required here. Record that the existing state tests remain the regression net for UI-fed values.

**Step 2: Build the minimal shell**

Implement UI bootstrap methods in `CombatStatusBar.cs` that:

- create a `Canvas` in `Screen Space - Overlay`
- add `CanvasScaler` with a `1920x1080` reference resolution
- add `GraphicRaycaster`
- create `SafeAreaRoot` and `BarRoot`
- cache references so the hierarchy is built once and reused

Remove or bypass `OnGUI()` so the old IMGUI bar no longer renders.

**Step 3: Run build verification**

Run: `dotnet build /Users/yxinyu/codes/BazaarPlusPlus/BazaarPlusPlus.csproj`

Expected: PASS

**Step 4: Commit**

```bash
git add Game/CombatStatusBar/CombatStatusBar.cs
git commit -m "feat: add combat status bar canvas shell"
```

### Task 3: Recreate the four-segment layout in uGUI

**Files:**
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`

**Step 1: Build the segment tree**

Create runtime helpers that construct:

- `TimeSegment` with label and value text
- `FrameSegment` with label and value text
- `MultiplierSegment` with left button, value text, and right button
- `PauseSegment` with label and button

Use `HorizontalLayoutGroup`, `LayoutElement`, anchors, and padding instead of absolute child rect math wherever practical.

**Step 2: Wire controls**

Connect:

- left multiplier button to `StepCombatSpeed(-1)`
- right multiplier button to `StepCombatSpeed(1)`
- pause button to `ToggleCombatPause()`

Preserve button disabled behavior at multiplier edges and outside combat.

**Step 3: Run tests and build**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Run: `dotnet build /Users/yxinyu/codes/BazaarPlusPlus/BazaarPlusPlus.csproj`

Expected: PASS

**Step 4: Commit**

```bash
git add Game/CombatStatusBar/CombatStatusBar.cs
git commit -m "feat: rebuild combat status bar layout in canvas"
```

### Task 4: Apply the approved visual polish

**Files:**
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`

**Step 1: Style the container and segments**

Implement the approved polish:

- layered capsule background
- softened separators
- secondary label styling and primary value styling
- standby and combat palette blending
- clear button `normal`, `disabled`, and `pressed` states
- distinct pause visuals for playing vs paused

Keep the visual system code data-driven where possible by centralizing colors, padding, font sizes, and transition constants.

**Step 2: Refresh per-frame UI state**

Update cached UI references in `Update()` or a dedicated refresh method so:

- text values stay in sync
- button interactability stays correct
- colors react to `_visualBlend`
- visibility respects `F6`, config enablement, and in-run gating

**Step 3: Run tests and build**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Run: `dotnet build /Users/yxinyu/codes/BazaarPlusPlus/BazaarPlusPlus.csproj`

Expected: PASS

**Step 4: Commit**

```bash
git add Game/CombatStatusBar/CombatStatusBar.cs
git commit -m "feat: polish combat status bar canvas visuals"
```

### Task 5: Update docs and verify in game

**Files:**
- Modify: `docs/combat-status-bar.md`
- Modify: `docs/plans/2026-03-13-combat-status-bar-canvas-design.md`

**Step 1: Update documentation**

Refresh runtime docs so they reflect:

- Canvas-based rendering instead of IMGUI
- the independent overlay canvas approach
- active pause behavior
- resolution-scaling expectations

**Step 2: Manual verification checklist**

Verify in game:

- `1080p`, `1440p`, and `4K` keep roughly the same relative bar size
- standby state renders with the cool palette
- combat state renders with the warm palette
- multiplier buttons step and disable correctly at bounds
- pause disables in standby, toggles during combat, and updates visual state
- `F6` still toggles the bar
- the bar does not cause obvious tooltip or popup layering regressions

**Step 3: Run final automated verification**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Run: `dotnet build /Users/yxinyu/codes/BazaarPlusPlus/BazaarPlusPlus.csproj`

Expected: PASS

**Step 4: Commit**

```bash
git add docs/combat-status-bar.md docs/plans/2026-03-13-combat-status-bar-canvas-design.md
git commit -m "docs: update combat status bar canvas design"
```
