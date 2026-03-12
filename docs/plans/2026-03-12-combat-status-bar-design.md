# Combat Status Bar Design

**Goal:** Replace the current bottom debug strip with a capsule-shaped combat playback controller that matches the approved sketch and keeps the first version in IMGUI.

## Context

The current `CombatStatusBar` mixes three concerns:

- overlay presentation
- combat playback runtime state
- combat speed control helpers stored globally in `ModState`

That shape made the first implementation quick, but it also locked the UI into a debug-toolbar layout and spread combat-specific behavior across unrelated global state.

## Approved UI

Render a bottom-centered horizontal capsule split into four fixed segments:

1. `Time`
   Show `--:--` in standby and logical combat time based on processed combat frames during combat.
2. `Frame`
   Show `Standby` out of combat and processed frame count only during combat.
3. `Multiplier`
   Show the active speed step in the middle with decrement and increment controls on the sides.
4. `Pause`
   Show a pause segment in a disabled visual state only. No functional pause behavior in this iteration.

The whole control remains an IMGUI overlay and still honors `F6` as a visibility toggle.
The same capsule stays visible in standby and combat, with the combat state visually emphasized.

## Behavioral Design

### Time

- Continue using logical combat time, not wall-clock playback time.
- Time is derived from `processedFrames * 50ms`.
- Outside combat, show `--:--`.

### Frame

- Do not show total frame count in the default UI.
- During combat, show processed frame count only.
- Outside combat, show `Standby`.

### Multiplier

- Replace the current row of preset buttons with step-based navigation.
- Use the existing discrete speed table: `0.25x`, `0.50x`, `1.00x`, `2.00x`, `3.00x`, `5.00x`.
- Left control moves to the previous step.
- Right control moves to the next step.
- Buttons are disabled at the boundaries.

### Pause

- Render as a dedicated segment with a pause icon.
- Use a disabled style so the UI communicates intent without exposing unfinished behavior.
- Do not change combat playback state when clicked.

## Architecture

Move combat-specific runtime state and control logic into `CombatStatusBar` as the owner of combat playback UI state.

`CombatStatusBar` should own:

- combat playback active flag
- current speed multiplier
- discrete speed steps
- processed frame count
- total frame count
- derived logical time
- speed-step navigation helpers

`ModState` should retain only cross-feature global state and config values that are not inherently part of the combat controller UI lifecycle.

Harmony patches and event listeners should talk to `CombatStatusBar` through a narrow static API instead of mutating `ModState` directly.

## Rendering Approach

Stay on IMGUI for this iteration.

Reasons:

- the current overlay is already implemented with `OnGUI`
- the redesign is primarily about layout and ownership, not a framework migration
- IMGUI is sufficient for a simple fixed-position controller

Implementation shape:

- manual `Rect` layout instead of `GUILayout`
- single rounded background box approximation with inner separators
- dedicated styles for segment labels, values, icon buttons, and disabled pause state
- a small blend value that shifts the controller palette between standby and active states

## Out of Scope

- actual pause / resume behavior
- frame stepping
- rewind
- drag-to-move UI
- replacing IMGUI with uGUI or UIToolkit
