# Single Upgrade Tooltip Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Change Bazaar++ hover upgrade preview to show a single tooltip while preserving native upgraded-value rendering.

**Architecture:** Reuse the existing hover patch and modifier-refresh controller, but replace the secondary-tooltip trigger with a primary-tooltip refresh that runs after entering native upgrade preview state. The native tooltip renderer will continue to derive upgraded values from `CardController.CanFuse()`.

**Tech Stack:** C#, Harmony, Unity MonoBehaviours, existing source-based regression tests

---

### Task 1: Add regression coverage for single-tooltip upgrade preview

**Files:**
- Modify: `tests/ItemEnchantPreview.Tests/Program.cs`

**Step 1: Write the failing test**

- Assert that `Patches/Tooltips/UpgradePreviewTooltipPatch.cs` no longer calls `DisplayUpgradeTooltips(...)`.
- Assert that the patch explicitly enters upgrade preview and refreshes the primary tooltip through `ShowCardTooltipController(...)`.

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

Expected: FAIL because the current implementation still calls `DisplayUpgradeTooltips(...)`.

**Step 3: Write minimal implementation**

- Update `UpgradePreviewTooltipPatch` to refresh the primary tooltip instead of requesting a secondary tooltip.
- Keep hotkey gating, item-only scope, and delayed scheduling.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

Expected: PASS

### Task 2: Keep runtime refresh behavior aligned with single-tooltip mode

**Files:**
- Modify: `Game/Tooltips/TooltipModifierRefreshController.cs`
- Modify: `docs/reference/upgrade-tooltip-implementation.md`

**Step 1: Update runtime refresh call sites**

- Ensure mode-change refresh still delegates to the upgrade preview patch helper after the primary tooltip is recreated.

**Step 2: Update reference documentation**

- Replace the dual-tooltip description with the single-tooltip refresh flow.

**Step 3: Re-run focused validation**

Run: `dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

Expected: PASS
