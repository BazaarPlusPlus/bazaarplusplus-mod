# BazaarPlusPlus Mouse Hotkey Support Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extend BazaarPlusPlus preview hotkeys so users can bind keyboard keys or mouse buttons, including side buttons, without changing the existing tooltip feature flow.

**Architecture:** Keep the current BazaarPlusPlus-owned hotkey backend and native-settings row injection. Expand validation, rebinding capture, and runtime hold detection from keyboard-only logic to shared keyboard-and-mouse button logic while continuing to reject continuous mouse controls such as scroll axes.

**Tech Stack:** C#, Harmony, Unity Input System, BepInEx config, existing source-based test projects

---

### Task 1: Add failing tests for mouse hotkey acceptance

**Files:**
- Modify: `tests/ItemEnchantPreview.Tests/Program.cs`
- Test: `tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

**Step 1: Write the failing source assertions**

Add assertions that require:

- `BppHotkeyService.cs` to reference `Mouse`
- `BppHotkeyService.cs` to recognize mouse button controls while rejecting scroll-style controls
- `BppKeyBindRowController.cs` to reference `Mouse.current`

**Step 2: Run the test project to verify it fails**

Run: `dotnet test tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

Expected: FAIL because current source only supports keyboard paths and keyboard rebinding.

### Task 2: Expand hotkey path validation and display handling

**Files:**
- Modify: `Game/Input/BppHotkeyService.cs`
- Test: `tests/ItemEnchantPreview.Tests/Program.cs`

**Step 1: Implement shared device-aware path normalization**

Refactor the hotkey service so the supported binding validator:

- accepts `<Keyboard>/...` key paths
- accepts `<Mouse>/...` button-control paths
- rejects non-button mouse controls such as scroll, position, and delta

**Step 2: Implement mouse-aware hold detection**

Update `IsHeld(...)` so it reads from the correct current device based on the binding path instead of assuming `Keyboard.current`.

**Step 3: Improve display names for mouse bindings**

Keep `Ctrl` and `Shift` aliases, and add readable names for common mouse buttons while falling back to Input System human-readable strings for the rest.

**Step 4: Run the test project to verify progress**

Run: `dotnet test tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

Expected: either still FAIL on rebinding assertions or PASS if Task 3 work is already complete.

### Task 3: Expand rebind capture to mouse buttons

**Files:**
- Modify: `Game/Input/BppKeyBindRowController.cs`
- Test: `tests/ItemEnchantPreview.Tests/Program.cs`

**Step 1: Write the minimal rebinding change**

Update the rebind `Update()` loop so it:

- still honors `Escape`
- still captures keyboard keys
- additionally inspects `Mouse.current` for button controls pressed this frame
- routes both devices through `BppHotkeyService.TrySetBindingPath(...)`

**Step 2: Preserve UI-state behavior**

Keep the existing display/edit object toggling, reset behavior, and warning flow unchanged.

**Step 3: Run the test project to verify it passes**

Run: `dotnet test tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

Expected: PASS.

### Task 4: Verify no tooltip consumer changes are required

**Files:**
- Read: `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- Read: `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
- Read: `Game/Tooltips/TooltipModifierRefreshController.cs`

**Step 1: Re-read the tooltip consumers**

Confirm they still depend only on `BppHotkeyService.IsHeld(...)` and therefore inherit mouse support without direct edits.

**Step 2: Record only necessary code changes**

Do not modify tooltip consumer files unless verification uncovers a real regression or compile issue.

### Task 5: Final verification

**Files:**
- Modify: `docs/plans/2026-03-23-bpp-mouse-hotkey-support-design.md` if implementation changes the approved design

**Step 1: Run the focused test project again**

Run: `dotnet test tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

Expected: PASS.

**Step 2: Run a source sanity pass on touched files**

Read the updated sections in:

- `Game/Input/BppHotkeyService.cs`
- `Game/Input/BppKeyBindRowController.cs`
- `tests/ItemEnchantPreview.Tests/Program.cs`

Check that the code still rejects non-button mouse controls and still allows keyboard defaults.

**Step 3: Commit**

```bash
git add docs/plans/2026-03-23-bpp-mouse-hotkey-support-design.md docs/plans/2026-03-23-bpp-mouse-hotkey-support.md Game/Input/BppHotkeyService.cs Game/Input/BppKeyBindRowController.cs tests/ItemEnchantPreview.Tests/Program.cs
git commit -m "feat: add mouse support for bpp preview hotkeys"
```
