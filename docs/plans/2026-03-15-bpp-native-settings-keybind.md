# BazaarPlusPlus Native Settings Keybind Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add BazaarPlusPlus hotkey settings to the native The Bazaar settings menu while keeping BazaarPlusPlus hotkey storage and runtime handling fully independent from the game's built-in keybinding backend.

**Architecture:** Inject BazaarPlusPlus rows into the native `OptionsDialogController` keybind area, but route all behavior through a new `BppHotkeyService` and `BppKeyBindRowController`. Existing BazaarPlusPlus features stop polling hard-coded keys and instead query the shared service for `WasPressed` or `IsHeld` semantics.

**Tech Stack:** C#, Harmony, Unity Input System, BepInEx config/state, NUnit test projects in `tests/`

---

### Task 1: Define BazaarPlusPlus hotkey action model

**Files:**
- Create: `Game/Input/BppActionId.cs`
- Modify: `Game/Input/KeyBindings.cs`
- Test: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`

**Step 1: Write the failing test**

Add a small test that references the new action identifiers and expected default keys through a public/internal helper API.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: FAIL because `BppActionId` or the helper API does not exist.

**Step 3: Write minimal implementation**

- Add `BppActionId` with:
  - `ToggleCombatStatusBar`
  - `HoldEnchantPreview`
  - `HoldUpgradePreview`
- Reduce `KeyBindings.cs` to compatibility helpers only, or remove hard-coded consumers from it if no longer needed.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Input/BppActionId.cs Game/Input/KeyBindings.cs tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs
git commit -m "refactor: define bpp hotkey action ids"
```

### Task 2: Build default binding and persistence model

**Files:**
- Create: `Game/Input/BppHotkeyBinding.cs`
- Create: `Game/Input/BppHotkeyService.cs`
- Create: `Game/Input/BppHotkeyStore.cs`
- Modify: `Models/ModState.cs`
- Test: `tests/MonsterLockToggleGate.Tests/MonsterLockToggleGate.Tests.csproj`
- Create: `tests/BppHotkeys.Tests/Program.cs`
- Create: `tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
- Create: `tests/BppHotkeys.Tests/BppHotkeyServiceTests.cs`

**Step 1: Write the failing test**

Create tests for:

- missing config returns defaults
- saved binding path overrides defaults
- reset to default removes override
- duplicate bindings are rejected

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: FAIL because the service and store do not exist.

**Step 3: Write minimal implementation**

- Define a binding record/model containing action id, default path, current path, and trigger mode
- Implement a store that reads/writes BazaarPlusPlus-owned keybinding data
- Implement `BppHotkeyService` APIs:
  - `GetBindingPath`
  - `GetBindingDisplay`
  - `TrySetBindingPath`
  - `ResetToDefault`
  - `IsDuplicate`

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Input/BppHotkeyBinding.cs Game/Input/BppHotkeyService.cs Game/Input/BppHotkeyStore.cs Models/ModState.cs tests/BppHotkeys.Tests
git commit -m "feat: add bpp hotkey persistence service"
```

### Task 3: Add runtime key state querying

**Files:**
- Modify: `Game/Input/BppHotkeyService.cs`
- Create: `tests/BppHotkeys.Tests/BppHotkeyRuntimeTests.cs`

**Step 1: Write the failing test**

Add tests that verify the service distinguishes:

- `WasPressed` for toggle-style actions
- `IsHeld` for hold-style actions
- unknown or unbound actions return false

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: FAIL because runtime query methods are incomplete.

**Step 3: Write minimal implementation**

- Add runtime query methods that use Unity Input System path lookup or a small wrapper around `Keyboard.current`
- Support normal keys such as `E`, `R`, and `F6`
- Keep the first version keyboard-only

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Input/BppHotkeyService.cs tests/BppHotkeys.Tests/BppHotkeyRuntimeTests.cs
git commit -m "feat: add bpp hotkey runtime queries"
```

### Task 4: Migrate combat status bar to the shared hotkey service

**Files:**
- Modify: `Game/CombatStatusBar/CombatStatusBar.cs`
- Modify: `Game/Input/KeyBindings.cs`
- Test: `tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs`

**Step 1: Write the failing test**

Add a test covering the status bar toggle entry point through a shim/helper so that the code no longer depends on `KeyBindings.Toggle.CombatStatusBar`.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: FAIL because the old hard-coded key path is still in use.

**Step 3: Write minimal implementation**

- Replace the direct `Keyboard.current` lookup in `CombatStatusBar.Update()`
- Query `BppHotkeyService.WasPressed(BppActionId.ToggleCombatStatusBar)`

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/CombatStatusBar/CombatStatusBar.cs Game/Input/KeyBindings.cs tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs
git commit -m "refactor: route combat status bar toggle through bpp hotkeys"
```

### Task 5: Migrate enchant and upgrade preview logic

**Files:**
- Modify: `Game/Tooltips/TooltipModifierRefreshController.cs`
- Modify: `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
- Test: `tests/ItemEnchantPreview.Tests/Program.cs`
- Create: `tests/BppHotkeys.Tests/TooltipHotkeyMappingTests.cs`

**Step 1: Write the failing test**

Add tests for:

- enchant preview mode activates when `HoldEnchantPreview` is held
- upgrade preview mode activates when `HoldUpgradePreview` is held
- upgrade preview coroutine exits when the hold key is released

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: FAIL because tooltip preview still depends on Ctrl/Shift.

**Step 3: Write minimal implementation**

- Replace modifier-specific helpers with `BppHotkeyService.IsHeld(...)`
- Keep existing tooltip refresh behavior intact

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Tooltips/TooltipModifierRefreshController.cs Patches/Tooltips/UpgradePreviewTooltipPatch.cs tests/BppHotkeys.Tests/TooltipHotkeyMappingTests.cs
git commit -m "refactor: route tooltip preview hotkeys through bpp hotkeys"
```

### Task 6: Implement BazaarPlusPlus keybind row controller

**Files:**
- Create: `Game/Input/BppKeyBindRowController.cs`
- Create: `Game/Input/BppKeybindLabelResolver.cs`
- Create: `tests/BppHotkeys.Tests/BppKeyBindRowControllerTests.cs`

**Step 1: Write the failing test**

Add tests for:

- current binding display text
- entering rebind state
- duplicate key rejection
- reset to default visibility logic

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: FAIL because the UI controller does not exist.

**Step 3: Write minimal implementation**

- Mirror the behavior of native `KeyBindController`
- Remove dependence on native `InputManager.Actions`
- Use `BppActionId`
- Save immediately after successful binding change

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Input/BppKeyBindRowController.cs Game/Input/BppKeybindLabelResolver.cs tests/BppHotkeys.Tests/BppKeyBindRowControllerTests.cs
git commit -m "feat: add bpp keybind row controller"
```

### Task 7: Inject BazaarPlusPlus keybind rows into native settings

**Files:**
- Create: `Patches/Settings/BppKeybindSettingsPatch.cs`
- Modify: `BazaarPlusPlus.csproj`
- Create: `tests/BppHotkeys.Tests/BppKeybindSettingsPatchTests.cs`

**Step 1: Write the failing test**

Add tests for:

- locating the native keybind area
- creating BazaarPlusPlus rows exactly once
- syncing labels and displayed bindings when the settings dialog reopens

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: FAIL because the patch does not exist.

**Step 3: Write minimal implementation**

- Patch `OptionsDialogController.Awake`
- Patch `OptionsDialogController.OnEnable`
- Clone a native keybind row template
- Remove native dependencies from the clone
- Attach `BppKeyBindRowController`

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Patches/Settings/BppKeybindSettingsPatch.cs BazaarPlusPlus.csproj tests/BppHotkeys.Tests/BppKeybindSettingsPatchTests.cs
git commit -m "feat: inject bpp keybinds into native settings"
```

### Task 8: Add logging, localization, and failure handling

**Files:**
- Modify: `Game/Input/BppHotkeyService.cs`
- Modify: `Game/Input/BppKeyBindRowController.cs`
- Modify: `Patches/Settings/BppKeybindSettingsPatch.cs`
- Create: `tests/BppHotkeys.Tests/BppKeybindLabelResolverTests.cs`

**Step 1: Write the failing test**

Add tests for:

- English and Simplified Chinese labels
- invalid saved path falls back to default
- settings injection failure logs without crashing

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: FAIL because localization and fallback handling are incomplete.

**Step 3: Write minimal implementation**

- Add label resolver
- Add fallback logging
- Ensure patch failures are contained and visible in logs

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: PASS

**Step 5: Commit**

```bash
git add Game/Input/BppHotkeyService.cs Game/Input/BppKeyBindRowController.cs Patches/Settings/BppKeybindSettingsPatch.cs tests/BppHotkeys.Tests/BppKeybindLabelResolverTests.cs
git commit -m "feat: harden bpp keybind settings flow"
```

### Task 9: Verify end-to-end behavior

**Files:**
- Modify: `README.md`
- Test: `tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
- Test: `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
- Test: `tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`

**Step 1: Run focused automated tests**

Run: `dotnet test tests/BppHotkeys.Tests/BppHotkeys.Tests.csproj`
Expected: PASS

**Step 2: Run existing impacted tests**

Run: `dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`
Expected: PASS

Run: `dotnet test tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`
Expected: PASS

**Step 3: Perform manual verification**

Check in game:

- BazaarPlusPlus rows appear in native settings
- changing `Combat Status Bar Toggle` affects runtime behavior
- changing `Show Enchant Preview` to `E` works
- changing `Show Upgrade Preview` to `R` works
- duplicate bindings are rejected
- reset restores defaults

**Step 4: Update docs**

Document the new settings location and default bindings in `README.md`.

**Step 5: Commit**

```bash
git add README.md
git commit -m "docs: document bpp native settings keybinds"
```
