# History Panel Dock Entry Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the `F8` HistoryPanel entry with a BazaarPlusPlus settings dock row that opens the existing HistoryPanel window only from lobby UI.

**Architecture:** Extend the settings dock row model so it can represent either a boolean toggle or an action row. Reuse the current `HistoryPanel` runtime/UI implementation and add a dock-specific open path instead of rebuilding the panel as a separate system.

**Tech Stack:** C#, Unity UI, BepInEx, Harmony, xUnit

---

### Task 1: Add a dock row action seam

**Files:**
- Modify: `Game/Settings/BppSettingsDockDefinition.cs`
- Test: `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
- Test: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`

**Step 1: Write the failing test**

Add tests covering:
- toggle-style dock rows report `ON/OFF` and flip state on activation
- action-style dock rows invoke their action and report dynamic status text

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

**Step 3: Write minimal implementation**

Refactor `BppSettingsDockDefinition` so each row provides:
- label resolver
- status resolver
- active-state resolver
- activate action
- collapse-after-activate flag

Keep toggle rows easy to construct from the existing `SettingsMenuToggleBridge`.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

### Task 2: Add Game History dock entry and remove F8

**Files:**
- Modify: `Game/Settings/BppSettingsDockCatalog.cs`
- Modify: `Game/Settings/BppSettingsDockController.cs`
- Modify: `Game/HistoryPanel/HistoryPanel.cs`
- Modify: `Plugin.cs`
- Modify: `Game/Input/KeyBindings.cs`
- Modify: `Patches/Settings/BppSettingsDockPatch.cs`

**Step 1: Write the failing test**

Add/extend tests for the dock row action API if needed for the `Game History` row wiring.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

**Step 3: Write minimal implementation**

- Add a `Game History` action row to the dock catalog
- Open the existing `HistoryPanel` from that row
- Collapse the settings dock after the action fires
- Remove `Plugin.Update()` hotkey handling and the `F8` keybinding
- Stop attaching the Bazaar++ settings dock in fight menu so the entry remains lobby-only

**Step 4: Run focused verification**

Run: `dotnet build BazaarPlusPlus.csproj`

### Task 3: Clean up related implementation debt

**Files:**
- Modify: `Game/HistoryPanel/HistoryPanel.cs`
- Modify: `Game/HistoryPanel/HistoryPanelController.cs`
- Modify: `Game/HistoryPanel/HistoryPanel.Canvas.cs`
- Modify: `Plugin.cs`
- Modify: `docs/reference/hotkeys-reference.md`
- Modify: `docs/run-logging.md`

**Step 1: Write the failing test**

Use the existing dock-row tests if the cleanup changes the row semantics.

**Step 2: Run test/build to verify current failure if any**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

**Step 3: Write minimal implementation**

- Cache filtered ghost battles per refresh instead of recreating the filtered list on every access
- Unpatch Harmony on plugin shutdown
- Remove stale docs mentioning `F8` as the HistoryPanel entry

**Step 4: Run final verification**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

Run: `dotnet build BazaarPlusPlus.csproj`
