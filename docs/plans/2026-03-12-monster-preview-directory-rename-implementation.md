# Monster Preview Directory Rename Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rename the broad `Game/Overlay` domain folder to `Game/MonsterPreview` and align the most ambiguous debug naming with that domain.

**Architecture:** This is a structural refactor only. File locations, explicit test project links, and the most confusing `OverlayDebugController` type name are updated to better reflect the stable `MonsterPreview` domain while keeping runtime behavior unchanged.

**Tech Stack:** C# 12, Unity, xUnit, MSBuild

---

### Task 1: Rename directories and update explicit project links

**Files:**
- Modify: `tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`
- Move: `Game/Overlay/** -> Game/MonsterPreview/**`
- Move: `tests/BazaarPlusPlus.Tests/Overlay/** -> tests/BazaarPlusPlus.Tests/MonsterPreview/**`

**Step 1: Write the failing test**

Use the existing build as the regression gate. The rename should break the test project links until paths are updated.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Expected: FAIL after moving files but before fixing links.

**Step 3: Write minimal implementation**

Update explicit compile links and any direct path references to the new `Game/MonsterPreview` and `tests/.../MonsterPreview` locations.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Expected: PASS

### Task 2: Rename the ambiguous debug controller

**Files:**
- Move/Modify: `Game/MonsterPreview/Debug/OverlayDebugController.cs`
- Modify: `Plugin.cs`
- Modify: `Game/DebugPanel/DebugPanel.cs`

**Step 1: Write the failing test**

Use the existing build as the regression gate. Renaming the type should fail compilation until all call sites are updated.

**Step 2: Run test to verify it fails**

Run: `dotnet build tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Expected: FAIL after renaming the controller type but before fixing call sites.

**Step 3: Write minimal implementation**

Rename `OverlayDebugController` to `MonsterPreviewDebugController` and update references.

**Step 4: Run tests and build**

Run: `dotnet test tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Run: `dotnet build tests/BazaarPlusPlus.Tests/BazaarPlusPlus.Tests.csproj`

Expected: PASS
