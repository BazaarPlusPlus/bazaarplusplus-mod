# Debug Inventory

## Goal

Record the current debug-related pieces in the mod and track what still needs cleanup after binding `BppLog.Debug(...)` to `ModState.IsDebug`.

## Current State

### 0. Current keyboard bindings

The mod currently listens to keyboard input in only two runtime UI places.

#### Debug panel keys

- [Game/DebugPanel/DebugPanel.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/DebugPanel/DebugPanel.cs)
- Current bindings:
  - `F2`: toggle debug panel visibility
  - `1`: switch to Summary section
  - `2`: switch to Preview section
  - `3`: switch to Run section
  - `4`: switch to Encounters section
  - `Tab`: toggle single-section vs all-sections mode

#### Combat status bar key

- [Game/CombatStatusBar/CombatStatusBar.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/CombatStatusBar/CombatStatusBar.cs)
- Current binding:
  - `F6`: toggle combat status bar visibility

#### Observations

- Key handling is no longer hard-coded inline at each call site.
- A central binding definition already exists in [Game/Input/KeyBindings.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/Input/KeyBindings.cs).
- `DebugPanel` and `CombatStatusBar` both read semantic bindings from `KeyBindings`.
- The current number keys are still context-specific because they mean "select debug panel section", not generic number actions.

### 1. Build-time debug switch

- `ModState.IsDebug` lives in [Models/ModState.cs](/Users/yxinyu/codes/BazaarPlusPlus/Models/ModState.cs).
- `BppLog.Debug(...)` in [Infrastructure/BppLog.cs](/Users/yxinyu/codes/BazaarPlusPlus/Infrastructure/BppLog.cs) already respects `ModState.IsDebug`.
- Result:
  - `Debug` build: debug logs are emitted.
  - `Release` build: debug logs are suppressed.

### 2. Debug UI mount point

- [Plugin.cs](/Users/yxinyu/codes/BazaarPlusPlus/Plugin.cs) now gates these components behind `ModState.IsDebug`:
  - `DebugPanel`
  - `MonsterPreviewDebugController`

Result:
- `Debug` build still gets debug UI and debug controls.
- `Release` build no longer mounts these debug-only components.

### 3. Debug panel

- [Game/DebugPanel/DebugPanel.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/DebugPanel/DebugPanel.cs)
- Current behavior:
  - Attached by `Plugin` only in debug builds.
  - Listens for `F2`, `1-4`, `Tab`.
  - Builds runtime snapshots for summary / preview / run / encounter sections.

Notes:
- This remains a developer-facing tool.
- The current code path no longer shows a keyboard `R` reset shortcut. The panel still supports `F2`, `1-4`, and `Tab`.

### 4. Monster preview debug controller

- [Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs)
- Current behavior:
  - Attached by `Plugin` only in debug builds.
  - Creates debug widget UI.
  - Enables preview debug options.
  - Supports anchor tuning and layout tuning.
  - Can switch preview source between monster DB and player hand.

Assessment:
- This is now correctly treated as debug-only at the runtime entry point.
- The remaining question is architectural separation, not runtime exposure.

### 5. Preview-board debug model still exists

- [Game/MonsterPreview/Architecture/PreviewBoardDebugOptions.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/Architecture/PreviewBoardDebugOptions.cs)
- [Game/MonsterPreview/View/BoardDebugOverlay.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/View/BoardDebugOverlay.cs)
- [Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs)
- [Game/MonsterPreview/Architecture/MonsterPreviewDefaults.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/Architecture/MonsterPreviewDefaults.cs)
- [Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs)

Current state:
- The preview system still carries explicit debug state:
  - `DebugEnabled`
  - `ShowAnchorPoint`
  - `ShowItemSlots`
  - `ShowSkillSlots`
  - `ShowCardBounds`
  - `ShowLabels`

Assessment:
- This is not necessarily wrong.
- These types may still be useful as internal structures.
- The problem is mainly that runtime debug entry points still drive them in all builds.

### 6. Debug visuals still exist in rendering code

- [Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs)
  - `DebugItemMarkerColor`
  - `DebugSkillMarkerColor`
  - debug marker material application
- [Game/MonsterPreview/Showcase/LockCanvasHoleOverlay.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/Showcase/LockCanvasHoleOverlay.cs)
  - `DebugBlockerColor`

Assessment:
- These are implementation leftovers or debug-oriented visuals.
- They are lower priority than the always-mounted debug controllers.

### 7. Remaining debug log call sites

Many files still call `BppLog.Debug(...)`, including:

- [Models/ModState.cs](/Users/yxinyu/codes/BazaarPlusPlus/Models/ModState.cs)
- [Data/MonsterDatabase.cs](/Users/yxinyu/codes/BazaarPlusPlus/Data/MonsterDatabase.cs)
- [Game/EncounterTracker.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/EncounterTracker.cs)
- [Game/GameDataReader.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/GameDataReader.cs)
- [Game/MonsterPreview/MonsterPreviewController.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/MonsterPreviewController.cs)
- [Game/MonsterPreview/EncounterTooltipPreviewBridge.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/EncounterTooltipPreviewBridge.cs)
- [Patches/NameOverride/NameOverridePatches.cs](/Users/yxinyu/codes/BazaarPlusPlus/Patches/NameOverride/NameOverridePatches.cs)
- [Patches/Showcase/ShowcaseTooltipPatches.cs](/Users/yxinyu/codes/BazaarPlusPlus/Patches/Showcase/ShowcaseTooltipPatches.cs)

Assessment:
- These are fine for now.
- They are already controlled by `ModState.IsDebug` through `BppLog.Debug(...)`.

## Main Problems

### Problem 1

The monster preview architecture still exposes debug-specific types and flags.

Impact:
- Not an immediate bug.
- But it keeps debug concepts mixed into production runtime structures.

### Problem 2

The inventory document can drift from the real code if it is not updated with implementation changes.

Impact:
- Cleanup priorities become misleading.
- Follow-up work can target already-solved issues.

## Recommended Cleanup Order

### Step 1

Add defensive guards inside:

- [Game/DebugPanel/DebugPanel.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/DebugPanel/DebugPanel.cs)
- [Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs)

Example direction:
- return early from `Update()` / `OnGUI()` when `!ModState.IsDebug`

This prevents accidental activation even if a component gets attached elsewhere later.

### Step 2

Re-evaluate whether preview-board debug types should remain always compiled:

- `PreviewBoardDebugOptions`
- `BoardDebugOverlay`
- `DebugEnabled`
- `MonsterPreviewDebugTuner`

Decision options:
- keep them as internal implementation details
- or isolate them more clearly under debug-only entry points

### Step 3

Keep using the existing central key binding definition in [Game/Input/KeyBindings.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/Input/KeyBindings.cs), and update this inventory whenever bindings change.

## Suggested End State

- `ModState.IsDebug` becomes the single source of truth for developer-facing debug behavior.
- `BppLog.Debug(...)` stays gated by `ModState.IsDebug`.
- `DebugPanel` is mounted only in debug builds from [Plugin.cs](/Users/yxinyu/codes/BazaarPlusPlus/Plugin.cs).
- `MonsterPreviewDebugController` is mounted only in debug builds from [Plugin.cs](/Users/yxinyu/codes/BazaarPlusPlus/Plugin.cs).
- Debug rendering options exist only when a debug controller explicitly enables them.
- keyboard bindings are defined centrally and referenced by semantic action names instead of raw key fields

## Summary

The biggest remaining issue is no longer runtime exposure of debug UI. That part is now gated in [Plugin.cs](/Users/yxinyu/codes/BazaarPlusPlus/Plugin.cs). The next cleanup question is architectural: whether monster preview debug-specific types should remain mixed into the production preview pipeline, even though their runtime entry points are now debug-only.
