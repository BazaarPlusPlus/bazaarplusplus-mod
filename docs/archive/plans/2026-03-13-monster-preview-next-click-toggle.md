# Monster Preview Next Click Toggle Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the next left or right mouse click close the active monster preview and consume that click.

**Architecture:** Keep the close-on-next-click state inside `MonsterLockShowcaseRuntime`, arm it when preview opens, and close the preview from a global mouse check in `Update()`. Keep the right-click lock patch as a same-frame fallback, and cover the gating logic with small pure-logic tests so the change stays isolated from Unity runtime dependencies.

**Tech Stack:** C#, Harmony patches, small `net10.0` executable tests

---

### Task 1: Add failing gate tests

**Files:**
- Modify: `tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj`
- Modify: `tests/MonsterLockToggleGate.Tests/Program.cs`
- Test: `tests/MonsterLockToggleGate.Tests/Program.cs`

**Step 1: Write the failing test**

Add tests that express:

- no active preview -> next click close gate returns `false`
- active preview + left click -> gate closes preview and returns `true`
- active preview + right click -> gate closes preview and returns `true`
- after the first consumed click, the next click returns `false`

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj`
Expected: FAIL because the new gate API does not exist yet.

**Step 3: Write minimal implementation**

Create a small pure-logic helper in the monster preview showcase area that models:

- whether preview is active
- whether the next click should close it
- whether a given mouse button should be consumed

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj tests/MonsterLockToggleGate.Tests/Program.cs
git commit -m "test: cover next-click monster preview toggle gate"
```

### Task 2: Wire runtime and patches

**Files:**
- Modify: `Game/MonsterPreview/Showcase/MonsterLockShowcaseController.cs`
- Modify: `Game/MonsterPreview/MonsterLockShowcaseRuntime.cs`
- Modify: `Patches/Showcase/ShowcaseTooltipPatches.cs`
- Modify: `Patches/Showcase/ShowcaseCardInteractionPatches.cs`
- Test: `tests/MonsterLockToggleGate.Tests/Program.cs`

**Step 1: Write the failing test**

Use the tests from Task 1 as the active failing spec and, if needed, extend them with one case proving the gate is armed only after preview activation.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj`
Expected: FAIL until runtime/controller logic is updated.

**Step 3: Write minimal implementation**

- arm the next-click close gate when preview opens successfully
- clear the gate when preview hides
- let `MonsterLockShowcaseRuntime.Update()` globally watch the next left/right mouse press
- keep `LockTooltipToggle` consulting the same gate as a right-click fallback
- remove the local left-click close logic from `ProceedClick`

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/MonsterPreview/Showcase/MonsterLockShowcaseController.cs Game/MonsterPreview/MonsterLockShowcaseRuntime.cs Patches/Showcase/ShowcaseTooltipPatches.cs Patches/Showcase/ShowcaseCardInteractionPatches.cs tests/MonsterLockToggleGate.Tests/Program.cs
git commit -m "feat: close monster preview on next click"
```

### Task 3: Verify targeted behavior

**Files:**
- Test: `tests/MonsterLockToggleGate.Tests/Program.cs`

**Step 1: Run targeted tests**

Run: `dotnet run --project tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj`
Expected: PASS

**Step 2: Run adjacent regression tests**

Run: `dotnet run --project tests/EncounterPreviewConversion.Tests/EncounterPreviewConversion.Tests.csproj`
Expected: PASS

**Step 3: Commit**

```bash
git add docs/plans/2026-03-13-monster-preview-next-click-toggle-design.md docs/plans/2026-03-13-monster-preview-next-click-toggle.md
git commit -m "docs: add monster preview next-click toggle plan"
```
