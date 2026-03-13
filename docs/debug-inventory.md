# Debug Inventory

## Goal

Record the current debug-related pieces in the mod and highlight what is still inconsistent after binding `BppLog.Debug(...)` to `ModState.IsDebug`.

## Current State

### 0. Current keyboard bindings

The mod currently listens to keyboard input in only two places.

#### Debug panel keys

- [Game/DebugPanel/DebugPanel.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/DebugPanel/DebugPanel.cs)
- Current bindings:
  - `F2`: toggle debug panel visibility
  - `1`: switch to Summary section
  - `2`: switch to Preview section
  - `3`: switch to Run section
  - `4`: switch to Encounters section
  - `Tab`: toggle single-section vs all-sections mode
  - `R`: reset panel state

#### Combat status bar key

- [Game/CombatStatusBar/CombatStatusBar.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/CombatStatusBar/CombatStatusBar.cs)
- Current binding:
  - `F6`: toggle combat status bar visibility

#### Observations

- Key handling is currently hard-coded inline at the call site.
- There is no central inventory of hotkeys.
- The code uses physical key names directly, not semantic action names.
- The current number keys are ambiguous outside the panel context because they mean "select section", not generic number actions.

### 1. Build-time debug switch

- `ModState.IsDebug` lives in [Models/ModState.cs](/Users/yxinyu/codes/BazaarPlusPlus/Models/ModState.cs).
- `BppLog.Debug(...)` in [Infrastructure/BppLog.cs](/Users/yxinyu/codes/BazaarPlusPlus/Infrastructure/BppLog.cs) already respects `ModState.IsDebug`.
- Result:
  - `Debug` build: debug logs are emitted.
  - `Release` build: debug logs are suppressed.

### 2. Debug UI is still always mounted

- [Plugin.cs](/Users/yxinyu/codes/BazaarPlusPlus/Plugin.cs) always adds:
  - `DebugPanel`
  - `MonsterPreviewDebugController`
- These components are not gated by `ModState.IsDebug`.

Result:
- `Release` build no longer prints debug logs.
- But debug UI and debug controls still exist at runtime.

### 3. Debug panel

- [Game/DebugPanel/DebugPanel.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/DebugPanel/DebugPanel.cs)
- Current behavior:
  - Always attached by `Plugin`.
  - Listens for `F2`, `1-4`, `Tab`, `R`.
  - Builds runtime snapshots for summary / preview / run / encounter sections.

Problem:
- This is still a developer-facing tool present in non-debug runtime.

### 4. Monster preview debug controller

- [Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs)
- Current behavior:
  - Always attached by `Plugin`.
  - Creates debug widget UI.
  - Enables preview debug options.
  - Supports anchor tuning and layout tuning.
  - Can switch preview source between monster DB and player hand.

Problem:
- This is still active debug-only functionality in `Release`.

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

`Debug` logging is now build-gated, but debug UI is not.

Impact:
- Runtime behavior is inconsistent.
- `Release` still contains interactive debug controls.

### Problem 2

Debug-only components are mounted unconditionally in `Plugin`.

Impact:
- Extra runtime surface area in `Release`.
- Possible accidental exposure of developer tools.

### Problem 3

The monster preview architecture still exposes debug-specific types and flags.

Impact:
- Not an immediate bug.
- But it keeps debug concepts mixed into production runtime structures.

### Problem 4

Keyboard bindings are scattered and encoded as raw key checks.

Impact:
- Harder to audit the full mod input surface.
- Harder to rename or remap shortcuts later.
- UI code is coupled to specific keys instead of semantic actions.

## Recommended Cleanup Order

### Step 1

Gate `DebugPanel` and `MonsterPreviewDebugController` in [Plugin.cs](/Users/yxinyu/codes/BazaarPlusPlus/Plugin.cs) with `ModState.IsDebug`.

This gives the highest value with the smallest change.

### Step 2

Add defensive guards inside:

- [Game/DebugPanel/DebugPanel.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/DebugPanel/DebugPanel.cs)
- [Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs)

Example direction:
- return early from `Update()` / `OnGUI()` when `!ModState.IsDebug`

This prevents accidental activation even if a component gets attached elsewhere later.

### Step 3

Re-evaluate whether preview-board debug types should remain always compiled:

- `PreviewBoardDebugOptions`
- `BoardDebugOverlay`
- `DebugEnabled`
- `MonsterPreviewDebugTuner`

Decision options:
- keep them as internal implementation details
- or isolate them more clearly under debug-only entry points

### Step 4

Extract a central `KeyBindings` definition and use semantic action names at call sites.

Recommended direction:

- create a small central type, for example `Game/Input/KeyBindings.cs`
- define bindings by meaning, not by raw UI wording
- let UI/controllers query semantic bindings instead of hard-coding `keyboard.f2Key`, `keyboard.f6Key`, etc.

Example shape:

```csharp
internal static class KeyBindings
{
    public static Key ToggleDebugPanel => Key.F2;
    public static Key SelectDebugSummary => Key.Digit1;
    public static Key SelectDebugPreview => Key.Digit2;
    public static Key SelectDebugRun => Key.Digit3;
    public static Key SelectDebugEncounters => Key.Digit4;
    public static Key ToggleDebugPanelViewMode => Key.Tab;
    public static Key ResetDebugPanelState => Key.R;
    public static Key ToggleCombatStatusBar => Key.F6;
}
```

Then the usage sites become semantically clearer:

- `ToggleDebugPanel`
- `SelectDebugSummary`
- `ToggleCombatStatusBar`

This is worth doing.

Reasoning:
- the current key surface is still small, so extraction is cheap
- the semantic names will make future cleanup easier
- once debug UI is gated by `ModState.IsDebug`, all debug-only keybindings will also become easier to isolate

## Suggested End State

- `ModState.IsDebug` becomes the single source of truth for developer-facing debug behavior.
- `BppLog.Debug(...)` stays gated by `ModState.IsDebug`.
- `DebugPanel` is mounted only in debug builds.
- `MonsterPreviewDebugController` is mounted only in debug builds.
- Debug rendering options exist only when a debug controller explicitly enables them.
- keyboard bindings are defined centrally and referenced by semantic action names instead of raw key fields

## Summary

The biggest remaining issue is not logging anymore. It is that `Release` still mounts and exposes the debug panel and monster preview debug controller. After that, the next cleanup win is to centralize keyboard bindings behind a small `KeyBindings` abstraction with semantic names.
