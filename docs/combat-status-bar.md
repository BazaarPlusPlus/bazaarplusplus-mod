# Combat Status Bar

## Goal

Add a lightweight combat playback controller that stays visible in both standby and combat states and shows:

- logical combat time
- current processed frame count without exposing total frame count
- current playback speed
- step-based speed controls
- a disabled pause segment for future expansion

This intentionally does not implement pause behavior, frame stepping, rewind, or a custom replay controller.

## Scope

Implemented behavior:

- show the same bottom-centered controller in standby and combat
- display standby content outside combat and active content during combat
- display logical combat time based on processed combat frames
- display frame progress as processed count only
- allow playback speed changes through discrete step buttons only
- keep the original game combat simulation loop intact

Explicitly out of scope:

- pause / resume implementation
- rewind one frame
- fast-forward one frame
- exposing or patching the local `watch` variable inside `CombatSimHandler.Simulate`
- replacing the full combat replay pipeline

## Files

- [Plugin.cs](/Users/yxinyu/codes/BazaarPlusPlus/Plugin.cs)
- [Game/CombatStatusBar/CombatStatusBar.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/CombatStatusBar/CombatStatusBar.cs)
- [Game/CombatStatusBar/CombatStatusBar.State.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/CombatStatusBar/CombatStatusBar.State.cs)
- [Game/CombatStatusBar/CombatStatusBar.Config.cs](/Users/yxinyu/codes/BazaarPlusPlus/Game/CombatStatusBar/CombatStatusBar.Config.cs)
- [Models/ModState.cs](/Users/yxinyu/codes/BazaarPlusPlus/Models/ModState.cs)
- [Patches/Combat/CombatSimulationPatches.cs](/Users/yxinyu/codes/BazaarPlusPlus/Patches/Combat/CombatSimulationPatches.cs)
- [Patches/Combat/CombatSpeedPatch.cs](/Users/yxinyu/codes/BazaarPlusPlus/Patches/Combat/CombatSpeedPatch.cs)

## Runtime Model

### UI component

`CombatStatusBar` is a `MonoBehaviour` added from `Plugin.Awake()`.

Responsibilities:

- subscribe to combat start/end events
- show and hide the bottom controller
- render standby and active content states
- render current logical time, frame progress, and speed
- expose combat playback state to Harmony patches
- allow the user to move between predefined speed steps

### Shared runtime state

`CombatStatusBar` stores the combat playback state needed by both the overlay and Harmony patches:

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
2. `CombatStatusBar.OnCombatStarted()` calls `CombatStatusBar.BeginCombatPlayback()`
3. the controller transitions from standby visuals to active visuals

### Combat end

Source:

- original game code triggers `Events.CombatEnded` near the end of `CombatSimHandler.Simulate()`

Mod flow:

1. `CombatStatusBar` listens to `Events.CombatEnded`
2. `CombatStatusBar.OnCombatEnded()` calls `CombatStatusBar.EndCombatPlayback()`
3. the controller transitions back to standby visuals

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

The controller therefore defines logical combat time as:

`logical time = processed frame count * 50ms`

This means:

- changing playback speed does not change the logical time shown
- the displayed time reflects progress on the combat simulation timeline
- outside combat, the controller shows `--:--`

### How processed frames are counted

The implementation deliberately avoids patching the local loop index inside `CombatSimHandler.Simulate()`, because that would be more fragile across game updates.

Instead:

1. total frame count is captured at the start of `CombatSimHandler.Simulate(NetMessageCombatSim, CancellationTokenSource)`
2. processed frame count is advanced by a postfix patch on `FinalBlowSlowDownController.Process(int framesLeft)`

This works because `FinalBlowSlowDownController.Process(...)` is invoked once per simulation frame inside the main combat loop.

Default UI behavior:

- during combat, show processed frame count only
- do not show total frame count in the default UI, to avoid telegraphing combat length
- outside combat, show `Standby`

## Speed Control Design

### Step-based speeds

The controller allows only discrete speed steps.

Current step list is stored in `CombatStatusBar.CombatSpeedSteps`.

At the moment the list is:

- `0.25x`
- `0.50x`
- `1.00x`
- `2.00x`
- `3.00x`
- `5.00x`

### Why step-based

Discrete speeds keep the behavior predictable:

- one compact multiplier control instead of a row of buttons
- fewer edge cases
- no arbitrary user-entered values
- easier balancing against the game’s own final-blow slowdown logic

### How the speed override works

The mod patches `CombatSimHandler.SetSpeed(float speed)` with a Harmony prefix.

When combat playback is active:

- the incoming speed argument is replaced with `CombatStatusBar.CombatSpeedMultiplier`

This means the game still uses its normal simulation loop, but speed selection is overridden by the mod’s chosen preset.

## Config

Current config entries:

- `Combat.EnableCombatStatusBar`
- `Combat.DefaultSpeedMultiplier`

Behavior:

- the default speed is loaded at startup
- selecting a step in the UI updates the active speed
- the selected speed is also written back to the config entry

## UI Behavior

Location:

- bottom center of the screen

Layout:

- four fixed segments: `Time | Frame | Multiplier | Pause`

State content:

- standby: `Time=--:--`, `Frame=Standby`, `Multiplier=current preset`, `Pause=disabled`
- combat: `Time=logical elapsed`, `Frame=processed only`, `Multiplier=current speed`, `Pause=disabled`

Visual transition:

- standby uses a darker, lower-contrast palette
- combat uses a brighter warm palette
- the controller blends between those palettes when combat starts or ends

Controls:

- left/right multiplier step buttons
- disabled pause button placeholder
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

- frame counting assumes `FinalBlowSlowDownController.Process(...)` remains one call per processed combat frame
- the controller tracks combat playback time only, not recap browsing or board transition time
- this is not a full replay controller and does not support frame stepping or rewind

## Future Extensions

Reasonable next steps if needed:

- show both logical time and real playback time side by side
- add a compact mode for the status bar
- add a small marker when the game enters final-blow slowdown
- persist the last selected preset more explicitly if config write-back is not sufficient
