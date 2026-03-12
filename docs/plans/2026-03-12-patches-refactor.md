# Patches Directory Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reorganize Harmony patch files under `Patches/` by feature without changing runtime behavior.

**Architecture:** Keep Harmony patches centralized under `Patches/`, but split them into feature-scoped subdirectories so each file owns a single concern. Leave runtime components, builders, and state outside `Patches/` to preserve the existing project layering.

**Tech Stack:** C#, .NET SDK, Harmony, BepInEx

---

### Task 1: Add feature-scoped patch files

**Files:**
- Create: `Patches/Combat/CombatSimulationPatches.cs`
- Create: `Patches/Combat/CombatSpeedPatch.cs`
- Create: `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- Create: `Patches/Showcase/ShowcaseCardInteractionPatches.cs`
- Create: `Patches/Showcase/ShowcaseTooltipPatches.cs`
- Create: `Patches/NameOverride/NameOverridePatches.cs`

**Step 1: Write the failing test**

No new behavior is introduced. Verification will use build and existing tests after file moves.

**Step 2: Run test to verify it fails**

Not applicable for this structural refactor.

**Step 3: Write minimal implementation**

Copy each existing patch class into its new feature-scoped file and keep namespaces, attributes, and logic unchanged.

**Step 4: Run test to verify it passes**

Run: `dotnet build`
Expected: build succeeds with the reorganized files.

**Step 5: Commit**

Deferred for this session.

### Task 2: Remove legacy catch-all files

**Files:**
- Delete: `Patches/Patches.cs`
- Delete: `Patches/ShowcaseCardPatches.cs`
- Delete: `Patches/NameOverridePatches.cs`

**Step 1: Write the failing test**

No new test. The build will catch duplicate type definitions if any legacy files remain.

**Step 2: Run test to verify it fails**

Not applicable for this structural refactor.

**Step 3: Write minimal implementation**

Delete the obsolete files after confirming their contents were migrated.

**Step 4: Run test to verify it passes**

Run: `dotnet build`
Expected: build succeeds and no duplicate type errors appear.

**Step 5: Commit**

Deferred for this session.

### Task 3: Run regression verification

**Files:**
- Verify: `BazaarPlusPlus.csproj`
- Verify: `tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

**Step 1: Write the failing test**

Use the existing automated suite as regression coverage.

**Step 2: Run test to verify it fails**

Not applicable unless the suite exposes a regression.

**Step 3: Write minimal implementation**

Do not change behavior unless verification finds a regression.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`
Expected: existing tests pass.

**Step 5: Commit**

Deferred for this session.
