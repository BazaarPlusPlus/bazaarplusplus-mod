# Monster Preview Dead Code Cleanup Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove confirmed-unused Monster Preview code paths and verify the project still compiles.

**Architecture:** Delete dead code in three groups: pure unreferenced helpers, the obsolete tooltip bridge path, and the unused `BoardView` branch. Then verify with fresh reference scans and a full project build so removal is backed by evidence instead of assumption.

**Tech Stack:** C#, Unity mod codebase, MSBuild, ripgrep

---

### Task 1: Remove confirmed dead code

**Files:**
- Delete: `Game/MonsterPreview/Architecture/CompositePreviewDataSource.cs`
- Delete: `Game/MonsterPreview/Anchor/AdjustableAnchorStrategy.cs`
- Delete: `Game/MonsterPreview/Anchor/AnchorAdjustment.cs`
- Delete: `Game/MonsterPreview/Showcase/LockCanvasHoleOverlay.cs`
- Delete: `Game/MonsterPreview/Showcase/LockCanvasHoleLayout.cs`
- Delete: `Game/MonsterPreview/EncounterTooltipPreviewBridge.cs`
- Delete: `Game/MonsterPreview/Architecture/PreviewBoardRequestFactory.cs`
- Delete: `Game/MonsterPreview/View/BoardView.cs`
- Delete: `Game/MonsterPreview/View/BoardDebugOverlay.cs`
- Delete: `Game/MonsterPreview/View/BoardLayoutSnapshot.cs`

**Step 1: Delete the requested files**

Use `apply_patch` delete hunks for the ten files.

**Step 2: Re-scan references**

Run `rg` for the deleted type names and confirm there are no remaining code references.

**Step 3: Build the project**

Run: `dotnet build /Users/yxinyu/codes/BazaarPlusPlus/BazaarPlusPlus.csproj`

Expected: build succeeds with exit code `0`.
