# Debug Panel Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rename `DebugOverlay` to `DebugPanel` and improve usability with section-based navigation, visible shortcuts, and collapsible encounter details.

**Architecture:** Split panel state/navigation into a small pure state class so its behavior can be tested without Unity GUI dependencies. Keep the MonoBehaviour focused on input handling, data projection, and rendering the new sectioned panel.

**Tech Stack:** C#, .NET SDK, Unity MonoBehaviour, Unity IMGUI, xUnit

---

### Task 1: Add state tests for panel navigation

**Files:**
- Create: `tests/BazaarPlusPlus.Tests/DebugPanelStateTests.cs`
- Modify: `tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`
- Create: `Game/DebugPanelState.cs`

**Step 1: Write the failing test**

Add tests covering default section, section switching, view mode toggling, reset behavior, and encounter expansion toggling.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter DebugPanelStateTests`
Expected: FAIL because `DebugPanelState` does not exist yet.

**Step 3: Write minimal implementation**

Implement the smallest `DebugPanelState` API needed by the tests.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter DebugPanelStateTests`
Expected: PASS

**Step 5: Commit**

Deferred for this session.

### Task 2: Replace DebugOverlay with DebugPanel

**Files:**
- Create: `Game/DebugPanel.cs`
- Delete: `Game/DebugOverlay.cs`
- Modify: `Plugin.cs`
- Modify: `Game/Overlay/Debug/OverlayDebugController.cs`

**Step 1: Write the failing test**

Use the existing `DebugPanelState` coverage to constrain the panel behavior while refactoring the MonoBehaviour.

**Step 2: Run test to verify it fails**

Not applicable beyond Task 1.

**Step 3: Write minimal implementation**

Port the existing overlay rendering into the new panel layout, add section navigation and collapsible encounters, and update component registration in `Plugin.cs`.

**Step 4: Run test to verify it passes**

Run: `dotnet build`
Expected: PASS

**Step 5: Commit**

Deferred for this session.

### Task 3: Run regression verification

**Files:**
- Verify: `tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

**Step 1: Write the failing test**

Use the existing automated suite as regression coverage.

**Step 2: Run test to verify it fails**

Not applicable unless the refactor breaks existing behavior.

**Step 3: Write minimal implementation**

Only fix regressions discovered by verification.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`
Expected: PASS

**Step 5: Commit**

Deferred for this session.
