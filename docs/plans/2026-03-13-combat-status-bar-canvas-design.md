# Combat Status Bar Canvas Design

**Goal:** Migrate `CombatStatusBar` from fixed-pixel IMGUI rendering to a runtime `Canvas` HUD while keeping the approved four-segment capsule layout and adding light UI polish.

## Context

The current implementation renders in `OnGUI()` with fixed pixel sizes and absolute offsets. That keeps the controller simple, but it has two concrete drawbacks:

- visual size changes noticeably across resolutions
- the control still reads like a debug overlay instead of a stable HUD element

The state and behavior model is already in a workable shape. `CombatStatusBar` owns combat playback state, exposes pure display helpers, and updates a visual blend between standby and combat. The migration should preserve that logic and replace only the rendering layer.

## Approved Direction

Keep the same bottom-centered capsule controller with four segments:

1. `Time`
2. `Frame`
3. `Multiplier`
4. `Pause`

The bar remains visible in standby and combat, still honors `F6`, still uses logical combat time, and still exposes speed stepping plus pause toggling.

This iteration does **not** redesign the information architecture. It upgrades the presentation quality and resolution behavior.

## Approach Options

### Option 1: Independent runtime Canvas

Create a dedicated `Screen Space - Overlay` canvas at runtime and build the full bar under it.

Pros:

- smallest migration risk
- no dependency on internal game UI hierarchy
- easiest way to guarantee predictable scaling

Cons:

- may need minor sorting-order tuning to coexist with game overlays

### Option 2: Attach to an existing game Canvas

Find a stable root canvas in the game UI tree and parent the bar under it.

Pros:

- naturally participates in the game UI layer stack

Cons:

- higher coupling to upstream UI structure
- more fragile against game updates

### Option 3: Stay on IMGUI and emulate scaling

Keep `OnGUI()` and add manual scale logic with `GUI.matrix`.

Pros:

- least code churn

Cons:

- keeps the debug-overlay rendering model
- layout remains harder to maintain
- polish and interaction quality still lag behind uGUI

## Recommendation

Use **Option 1**.

It solves the resolution problem directly, keeps the migration self-contained, and gives the feature a proper HUD foundation without taking on upstream UI hierarchy risk. If later testing shows sorting-order issues, those can be fixed without reworking the whole bar.

## Runtime Architecture

`CombatStatusBar` remains the owner of:

- overlay visibility
- combat playback active state
- paused state
- speed multiplier
- processed and total frame counts
- logical elapsed time
- palette blend progression

The new rendering layer lives inside the same component and adds two responsibilities:

- build the runtime UI tree once
- refresh cached `Text`, `Image`, and `Button` elements when state changes

This keeps combat state, Harmony integration, and keybind behavior unchanged. The only major code movement is replacing `OnGUI()` drawing with UI construction and refresh methods.

## UI Structure

Recommended runtime tree:

- `CombatStatusBarCanvas`
- `SafeAreaRoot`
- `BarRoot`
- `TimeSegment`
- `FrameSegment`
- `MultiplierSegment`
- `PauseSegment`

Implementation details:

- `CombatStatusBarCanvas` uses `Canvas`, `CanvasScaler`, and `GraphicRaycaster`
- `BarRoot` is anchored bottom-center
- the bar uses a fixed reference layout width with scale handled by `CanvasScaler`
- segments are arranged with `HorizontalLayoutGroup`
- each segment owns its own label/value or button/value/button subtree

`MultiplierSegment` keeps step-left, current value, and step-right.
`PauseSegment` remains part of the bar and now clearly reflects three states:

- standby disabled
- combat playing
- combat paused

## Visual Design Rules

Preserve the current silhouette and state language:

- single capsule-like bottom controller
- colder, darker standby palette
- warmer, brighter combat palette
- smooth color blend between standby and combat

Upgrade the presentation in controlled ways:

- use a layered background instead of a single flat IMGUI fill
- soften segment separators so they feel structural rather than debug-grid lines
- keep labels secondary and values primary
- add clear button states for `normal`, `disabled`, and `pressed`
- make `Pause` visually change when toggled, not only by swapping glyphs

Explicitly out of scope:

- full game-native UI reproduction
- new icon asset pipeline
- complex hover effects or animated flourishes
- extra control segments or secondary metadata

## Input and Interaction

- `F6` keeps toggling visibility
- multiplier buttons remain step-based and clamp at the edges
- pause remains disabled outside combat
- pause toggles live combat playback state during combat

The migration relies on the game's existing `EventSystem`. If testing shows inconsistent pointer handling, the fallback is to explicitly validate or provision a compatible runtime event module without changing the bar structure.

## Risks

### Canvas layering

An independent overlay canvas may render above or below some game overlays in undesirable ways.

Mitigation:

- start with a conservative sorting order
- verify against combat HUD, tooltip flows, and transient popups

### Font fidelity

Using only default runtime font assets may improve layout but still look less integrated than the game UI.

Mitigation:

- prioritize spacing, contrast, and button states first
- revisit font sourcing only if the result still reads as obviously foreign

### Interaction routing

Buttons depend on the active UI event pipeline.

Mitigation:

- verify click handling in combat and standby
- confirm the bar does not block unrelated interactions outside its own bounds

## Verification Criteria

The redesign is successful when all of the following are true:

- the bar keeps a stable relative size at `1080p`, `1440p`, and `4K`
- standby and combat visual states still feel distinct
- multiplier stepping behavior matches current logic
- pause is disabled outside combat and usable during combat
- `F6` visibility toggle still works
- no combat patch behavior regresses
- no obvious UI overlap or input-routing regression appears in normal combat flows
