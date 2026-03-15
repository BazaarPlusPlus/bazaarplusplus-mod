# Namespace Alignment Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Align self-contained `Game/*` subsystems with feature namespaces while keeping the assembly root namespace as `BazaarPlusPlus`.

**Architecture:** Preserve `BazaarPlusPlus` as the shared root for cross-cutting code, but move `Game/CombatStatusBar/*` and `Game/MonsterPreview/*` into explicit feature namespaces. Update tests and integration points at the same time so linked-source test projects and Harmony patch entry points continue to compile cleanly.

**Tech Stack:** C# 12, .NET `netstandard2.1`, xUnit, linked-source test projects, BepInEx/Harmony integration

---

### Task 1: Express the target `CombatStatusBar` namespace in tests

**Files:**
- Modify: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`
- Modify: `tests/CombatStatusBarState.Tests/TestCombatStatusBarShims.cs`
- Test: `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`

**Step 1: Write the failing test/import change**

Add `using BazaarPlusPlus.Game.CombatStatusBar;` to the combat status bar test project sources so they describe the intended production namespace.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: compile failure because `CombatStatusBar` still lives in `BazaarPlusPlus`.

**Step 3: Write minimal implementation**

Move the linked production `CombatStatusBar` source files from `namespace BazaarPlusPlus;` to `namespace BazaarPlusPlus.Game.CombatStatusBar;` and update any production callers that reference `CombatStatusBar`.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: PASS.

**Step 5: Commit**

```bash
git add tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs \
  tests/CombatStatusBarState.Tests/TestCombatStatusBarShims.cs \
  Game/CombatStatusBar/*.cs \
  Game/Input/KeyBindings.cs \
  Patches/Combat/*.cs \
  docs/plans/2026-03-15-namespace-alignment.md
git commit -m "refactor: move combat status bar into feature namespace"
```

### Task 2: Express the target `MonsterPreview` namespace in a linked-source test project

**Files:**
- Modify: `tests/MonsterPreviewResilience.Tests/Program.cs`
- Modify: `tests/MonsterPreviewResilience.Tests/TestLocalCardTemplateCatalog.cs`
- Test: `tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`

**Step 1: Write the failing test/import change**

Add `using BazaarPlusPlus.Game.MonsterPreview;` to the resilience test sources so the test project declares the desired feature namespace dependency.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`
Expected: compile failure because the linked production types still live in `BazaarPlusPlus`.

**Step 3: Write minimal implementation**

Move `Game/MonsterPreview/**` production files from `namespace BazaarPlusPlus;` to `namespace BazaarPlusPlus.Game.MonsterPreview;` and update production references from patches, data sources, and controllers.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`
Expected: PASS.

**Step 5: Commit**

```bash
git add tests/MonsterPreviewResilience.Tests/Program.cs \
  tests/MonsterPreviewResilience.Tests/TestLocalCardTemplateCatalog.cs \
  Game/MonsterPreview/**/*.cs \
  Game/EncounterPreviewSpecConverter.cs \
  Patches/Showcase/*.cs
git commit -m "refactor: move monster preview into feature namespace"
```

### Task 3: Verify the integrated build graph

**Files:**
- Verify: `BazaarPlusPlus.csproj`
- Verify: `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
- Verify: `tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`

**Step 1: Run targeted project verification**

Run:

```bash
dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj
dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
```

Expected: both pass without namespace-related compile errors.

**Step 2: Run a broader compile verification**

Run: `dotnet build BazaarPlusPlus.csproj`
Expected: build succeeds if local game assembly references are available; otherwise record the environment limitation and keep the targeted tests as proof.

**Step 3: Commit**

```bash
git add .
git commit -m "chore: verify namespace alignment"
```
