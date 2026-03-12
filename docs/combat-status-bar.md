# Combat Status Bar

## Goal

Add a lightweight combat status bar that appears during combat playback and shows:

- logical combat time
- current processed frame and total frame count
- current playback speed
- preset speed buttons

This intentionally does not implement frame stepping, rewind, or a custom replay controller.

## Scope

Implemented behavior:

- show a bottom status bar only while combat playback is active
- display logical combat time based on processed combat frames
- display frame progress as `processed/total`
- allow playback speed changes through preset buttons only
- keep the original game combat simulation loop intact

Explicitly out of scope:

- rewind one frame
- fast-forward one frame
- exposing or patching the local `watch` variable inside `CombatSimHandler.Simulate`
- replacing the full combat replay pipeline

## Files

- [Plugin.cs](/C:/Users/cauyx/Desktop/codes/BazaarPlannerMod/Plugin.cs)
- [Game/CombatStatusBar.cs](/C:/Users/cauyx/Desktop/codes/BazaarPlannerMod/Game/CombatStatusBar.cs)
- [Models/ModState.cs](/C:/Users/cauyx/Desktop/codes/BazaarPlannerMod/Models/ModState.cs)
- [Patches/Patches.cs](/C:/Users/cauyx/Desktop/codes/BazaarPlannerMod/Patches/Patches.cs)

## Runtime Model

### UI component

`CombatStatusBar` is a `MonoBehaviour` added from `Plugin.Awake()`.

Responsibilities:

- subscribe to combat start/end events
- show and hide the bottom overlay
- render current logical time, frame progress, and speed
- allow the user to select from predefined speed presets

### Shared runtime state

`ModState` stores the combat playback state needed by both the overlay and Harmony patches:

- `CombatPlaybackActive`
- `CombatSpeedMultiplier`
- `CombatSpeedSteps`
- `ProcessedCombatFrames`
- `TotalCombatFrames`

It also exposes helper methods:

- `BeginCombatPlayback()`
- `EndCombatPlayback()`
- `SetCombatFrameTotal(int totalFrames)`
- `AdvanceCombatFrame()`
- `SetCombatSpeed(float speed)`
- `GetCombatLogicalElapsed()`

## Event Flow

### Combat start

Source:

- original game code triggers `Events.CombatStarted` from `CombatSimHandler.Simulate()`

Mod flow:

1. `CombatStatusBar` listens to `Events.CombatStarted`
2. `CombatStatusBar.OnCombatStarted()` calls `ModState.BeginCombatPlayback()`
3. the status bar becomes visible if enabled

### Combat end

Source:

- original game code triggers `Events.CombatEnded` near the end of `CombatSimHandler.Simulate()`

Mod flow:

1. `CombatStatusBar` listens to `Events.CombatEnded`
2. `CombatStatusBar.OnCombatEnded()` calls `ModState.EndCombatPlayback()`
3. the status bar stops rendering

## Logical Time Design

### Why not use `Stopwatch`

The original `CombatSimHandler.Simulate()` method uses a local `Stopwatch watch` to control playback pacing and to log the final elapsed playback time.

That value is not suitable for the status bar because:

- it is a local variable, not a stable public state source
- it measures actual playback time, not logical combat time
- playback time changes when speed changes

### What logical time means here

The combat simulation loop advances one simulation frame at a time.

From the decompiled runtime:

- each frame is scheduled at a base interval of `50ms`
- that means the simulation runs at a base rate of `20 frames/second`

The status bar therefore defines logical combat time as:

`logical time = processed frame count * 50ms`

This means:

- changing playback speed does not change the logical time shown
- the displayed time reflects progress on the combat simulation timeline

### How processed frames are counted

The implementation deliberately avoids patching the local loop index inside `CombatSimHandler.Simulate()`, because that would be more fragile across game updates.

Instead:

1. total frame count is captured at the start of `CombatSimHandler.Simulate(NetMessageCombatSim, CancellationTokenSource)`
2. processed frame count is advanced by a postfix patch on `FinalBlowSlowDownController.Process(int framesLeft)`

This works because `FinalBlowSlowDownController.Process(...)` is invoked once per simulation frame inside the main combat loop.

## Speed Control Design

### Preset-only speeds

The status bar allows only preset speeds.

Current preset list is stored in `ModState.CombatSpeedSteps`.

At the moment the list is:

- `0.25x`
- `0.5x`
- `1x`
- `2x`
- `3x`
- `4x`

### Why preset-only

Preset-only speeds keep the behavior predictable:

- fewer UI controls
- fewer edge cases
- no arbitrary user-entered values
- easier balancing against the game’s own final-blow slowdown logic

### How the speed override works

The mod patches `CombatSimHandler.SetSpeed(float speed)` with a Harmony prefix.

When combat playback is active:

- the incoming speed argument is replaced with `ModState.CombatSpeedMultiplier`

This means the game still uses its normal simulation loop, but speed selection is overridden by the mod’s chosen preset.

## Config

Current config entries:

- `Combat.EnableStatusBar`
- `Combat.DefaultSpeedMultiplier`

Behavior:

- the default speed is loaded at startup
- selecting a preset in the UI updates the active speed
- the selected speed is also written back to the config entry

## UI Behavior

Location:

- bottom center of the screen

Displayed values:

- `Sim`: logical combat time
- `Frame`: processed frame progress
- `Speed`: current active multiplier

Controls:

- preset buttons only
- `F6` toggles the visibility of the bar

## Tradeoffs

### Chosen tradeoff

Prefer stable hooks over exact access to internal loop variables.

Benefits:

- less dependent on IL layout
- lower chance of breaking on minor upstream refactors
- easier to reason about and maintain

Cost:

- processed frame counting depends on `FinalBlowSlowDownController.Process(...)` continuing to be called once per frame

### Not chosen

Patch the local frame index `i` inside `CombatSimHandler.Simulate()`.

Reason:

- higher maintenance cost
- more likely to break after upstream changes
- would require a more invasive patch strategy

## Known Limitations

- the implementation has not been compiled in this environment because no usable .NET SDK is available here
- frame counting assumes `FinalBlowSlowDownController.Process(...)` remains one call per processed combat frame
- the bar tracks combat playback time only, not recap browsing or board transition time
- this is not a full replay controller and does not support frame stepping or rewind

## Future Extensions

Reasonable next steps if needed:

- show both logical time and real playback time side by side
- add a compact mode for the status bar
- add a small marker when the game enters final-blow slowdown
- persist the last selected preset more explicitly if config write-back is not sufficient
