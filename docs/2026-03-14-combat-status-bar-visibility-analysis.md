# 2026-03-14 CombatStatusBar Visibility Analysis

## Problem

During runtime verification, `CombatStatusBar` appeared to stop rendering entirely.

The immediate concern was whether the recent standby-text change caused the whole bar to disappear.

## Scope Checked

The investigation covered:

- recent `CombatStatusBar` code changes
- current render gate conditions
- plugin load and config state in the actual game directory
- build/deploy status of the current DLL
- BepInEx runtime log output

## Confirmed Findings

### 1. The recent standby change does not affect visibility

The recent change only added:

- `LastCombatLogicalElapsed`
- `HasCompletedCombatPlayback`
- a dynamic time label getter
- standby text fallback logic

It did not change `ShouldRenderForState(...)`, canvas creation, or the component attach path.

Relevant files:

- `Game/CombatStatusBar/CombatStatusBar.State.cs`
- `Game/CombatStatusBar/CombatStatusBar.Canvas.cs`

### 2. The plugin and CombatStatusBar config both loaded correctly

Runtime log confirmed:

- plugin load succeeded
- `CombatStatusBar.InitializeConfig(...)` ran
- config resolved as `enabled=True`

Observed config in the live game directory:

- `C:\Program Files (x86)\Steam\steamapps\common\The Bazaar\BepInEx\config\BazaarPlusPlus.cfg`

Observed value:

- `Enabled = true`

This rules out "the bar is disabled in config" as the cause.

### 3. The built DLL was deployed to the live game path

`dotnet build` succeeded, and the project post-build target copied the new DLL into the BepInEx plugins folder.

This rules out "the running game is still using an old DLL" as the likely cause.

### 4. No CombatStatusBar exception showed up in the BepInEx log

The log showed normal plugin initialization, but no `CombatStatusBar` exception or render failure.

That makes a hard runtime crash inside the status bar unlikely.

## Most Likely Cause

The current visibility gate is:

```csharp
return overlayVisible && enabled && ModState.IsInGameRun;
```

Location:

- `Game/CombatStatusBar/CombatStatusBar.State.cs`

This means the bar can be fully hidden even when:

- the component exists
- the config is enabled
- the canvas is valid

if `ModState.IsInGameRun` is `false`.

## Why `IsInGameRun` Is Fragile

`ModState.IsInGameRun` is currently maintained only through lifecycle events:

- `Events.RunStarted`
- `Events.RunEnded`
- `Events.RunInterrupted`

Location:

- `Models/ModState.cs`

This is fragile because the state can be wrong when:

- the player enters an already-active run without a fresh `RunStarted`
- the game restores or resumes state through a path that does not emit `RunStarted`
- subscription timing misses the event

In those cases, the run is real, but the mod still believes it is outside a run, so `CombatStatusBar` never renders.

## Conclusion

The investigation does not support the theory that the standby text change broke visibility.

The higher-probability root cause is the existing dependency on `ModState.IsInGameRun`, where that flag can be stale even though the player is actually in a run.

## Recommended Fix

Do not rely exclusively on run lifecycle events to drive `IsInGameRun`.

Instead, add a secondary source of truth that derives in-run state from current live game state, for example:

1. inspect `Data.CurrentState`
2. map known gameplay states to "inside a run"
3. refresh `ModState.IsInGameRun` from that state on a reliable cadence or at stable entry points
4. keep the existing event listeners as fast-path updates, not the only authority

## Practical Fix Direction

Preferred approach:

- introduce a helper such as `ModState.RefreshRunStateFromCurrentState()`
- call it from a safe runtime location that already updates continuously or during screen/state transitions
- preserve existing `RunStarted/RunEnded/RunInterrupted` listeners, but let live state reconciliation correct missed transitions

## Expected Result After Fix

After reconciliation is added:

- `CombatStatusBar` should still stay hidden in menus and outside runs
- `CombatStatusBar` should appear even when entering a resumed or restored run path
- missed lifecycle events should no longer permanently hide the bar

## Current Status

The issue later stopped reproducing, which is consistent with an event/state timing problem rather than a deterministic rendering regression in the recent UI text change.
