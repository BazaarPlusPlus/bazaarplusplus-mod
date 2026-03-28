# Combat Status Bar

## Scope

The current implementation adds a bottom-centered combat HUD that is attached from
`Plugin.Awake()`. Runtime state is split between:

- `CombatStatusBar`, which owns UI, persisted local state, and input handling
- `CombatStatusBarModule`, which consumes combat events through `BppRuntimeHost.EventBus`

Implemented behavior:

- render only while `BppRuntimeHost.RunContext.IsInGameRun` and the feature is enabled
- show logical combat time derived from processed combat frames
- show processed frame count during combat and a standby / last-combat summary outside combat
- allow only discrete speed steps: `0.25x`, `0.33x`, `0.50x`, `1.00x`
- allow pause toggling through `GameServiceManager.PauseOrUnpauseGame(...)`
- persist enabled, visible, and default-speed config

Explicitly out of scope:

- frame stepping
- rewind
- custom replay controls
- arbitrary or faster-than-native speed overrides

## Key Files

- [Plugin.cs](../Plugin.cs)
- [Core/Runtime/BppRuntimeHost.cs](../Core/Runtime/BppRuntimeHost.cs)
- [Core/Config/BppConfig.cs](../Core/Config/BppConfig.cs)
- [Game/CombatStatusBar/CombatStatusBar.cs](../Game/CombatStatusBar/CombatStatusBar.cs)
- [Game/CombatStatusBar/CombatStatusBar.State.cs](../Game/CombatStatusBar/CombatStatusBar.State.cs)
- [Game/CombatStatusBar/CombatStatusBar.Config.cs](../Game/CombatStatusBar/CombatStatusBar.Config.cs)
- [Game/CombatStatusBar/CombatStatusBarModule.cs](../Game/CombatStatusBar/CombatStatusBarModule.cs)
- [Game/CombatStatusBar/CombatStatusBar.SettingsMenuBridge.cs](../Game/CombatStatusBar/CombatStatusBar.SettingsMenuBridge.cs)
- [Game/Input/KeyBindings.cs](../Game/Input/KeyBindings.cs)
- [Patches/Combat/CombatSimulationPatches.cs](../Patches/Combat/CombatSimulationPatches.cs)
- [Patches/Combat/CombatSpeedPatch.cs](../Patches/Combat/CombatSpeedPatch.cs)
- [Patches/Combat/CombatStatusBarSettingsPatch.cs](../Patches/Combat/CombatStatusBarSettingsPatch.cs)

## Runtime Flow

1. `CombatSimPatch` publishes `CombatSimObserved` at the start of
   `CombatSimHandler.Simulate(...)`.
2. `CombatStatusBarModule` reads the incoming `NetMessageCombatSim`, stores
   `TotalCombatFrames`, and updates `BppRuntimeHost.RunContext.LastVictoryCondition`.
3. `CombatFrameAdvancePatch` publishes `CombatFrameAdvanced` once per processed combat frame.
4. `CombatStatusBarModule` calls `CombatStatusBar.AdvanceCombatFrame()` to keep UI state in sync.
5. `CombatStatusBar` listens to `Events.CombatStarted` / `Events.CombatEnded`, builds the runtime
   canvas, and refreshes the HUD every `Update()`.

## Logical Time

Logical combat time is defined as:

`ProcessedCombatFrames * 50ms`

That keeps the display tied to simulation progress instead of wall-clock playback time. Speed
changes therefore do not distort the time label, and the counter remains stable across pause and
final-blow slowdown.

## Input And Config

- `KeyBindings.Toggle.CombatStatusBar` (`F6`) toggles the overlay visibility.
- Native settings integration is provided through the combat-status-bar settings bridge and patch.
- Config is read from `BppConfig`:
  - `EnableCombatStatusBarConfig`
  - `VisibleCombatStatusBarConfig`
  - `CombatStatusBarSpeedMultiplierConfig`

Configured speed is normalized to the supported step list. `CombatSpeedPatch` only overrides
requested speeds up to `1.00x`, which preserves native fast-forward / slowdown paths that the game
applies on its own.
