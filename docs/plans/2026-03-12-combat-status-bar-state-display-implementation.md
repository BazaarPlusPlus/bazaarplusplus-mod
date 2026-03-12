# Combat Status Bar Stateful Display Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Keep the same capsule controller visible in and out of combat, with distinct standby and active content plus a clearer visual transition.

**Architecture:** `CombatStatusBar` keeps owning combat playback state, but now also exposes pure display helpers that describe what the controller should show in standby versus combat. The IMGUI layer reads those helpers and renders the same four segments with different content, color emphasis, and a small animated blend when combat starts or ends.

**Tech Stack:** C# 12, Unity IMGUI, Harmony, xUnit

---

### Task 1: Add failing tests for display-state helpers

**Files:**
- Modify: `tests/BazaarPlusPlus.Tests/CombatStatusBarStateTests.cs`
- Test: `tests/BazaarPlusPlus.Tests/CombatStatusBarStateTests.cs`

**Step 1: Write the failing test**

Add tests for:

- standby frame text is `Standby`
- active frame text shows processed frames only, not `processed/total`
- standby time text is `--:--`
- visual blend helper moves toward active and standby targets

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: FAIL because the display helpers do not exist yet.

**Step 3: Write minimal implementation**

Add pure helper methods on `CombatStatusBar` that format frame text, time text, and blend progression without Unity GUI dependencies.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: PASS

### Task 2: Update the runtime UI to stay visible in standby

**Files:**
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`

**Step 1: Write the failing test**

Re-use the pure helper tests from Task 1 so the runtime UI can consume stable display data.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj --filter CombatStatusBarStateTests`

Expected: PASS from Task 1. Runtime rendering itself remains manually verified.

**Step 3: Write minimal implementation**

- stop hiding the controller outside combat
- render standby content when not in combat
- render active content when in combat
- use a visual blend value to shift colors and emphasis between standby and active states

**Step 4: Run tests and build**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Run: `dotnet build tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Expected: PASS

### Task 3: Refresh docs

**Files:**
- Modify: `docs/combat-status-bar.md`
- Modify: `docs/plans/2026-03-12-combat-status-bar-design.md`

**Step 1: Update documentation**

Document:

- same capsule in standby and combat
- standby text `--:--` and `Standby`
- active frame text as processed count only
- clearer visual transition between states

**Step 2: Run final verification**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Run: `dotnet build tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Expected: PASS
