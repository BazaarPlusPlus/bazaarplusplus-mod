# Saved Replay Debugging Notes

## Summary

This note captures the full saved-replay debugging trail after the initial offline bootstrap work landed.

Current status at the end of this session:

- saved replays can bootstrap from the lobby
- the native `ReplayState` path is entered successfully
- the replay can auto-start playback
- player cards can now be reconstructed and instantiated
- the remaining blocker is incomplete player and hero state during playback and replay teardown

The next session should focus on restoring player-state and hero-state data, not on replay bootstrap or frame pacing.

## Initial Failures

The first saved-replay implementation failed before the replay even started.

Confirmed problems:

1. `CombatSequenceCreated` was triggered from a stale delegate snapshot.
2. The opening `NetMessageGameSim` was injected through the wrong path.
3. Replay host resolution depended on the wrong native type lookup.

Symptoms seen in logs:

- `SocketBehavior is unavailable`
- saved replay request returned to the lobby immediately
- `ReplayState` never received the saved sequence

## Host Resolution Findings

Reflection against the actual game assemblies showed that the replay host used by the native runtime is `Networking.NetworkManager`, while legacy decompiled files still referenced `Networking.SocketBehavior`.

Fix applied:

- prefer `Networking.NetworkManager`
- keep old-name fallback only as a compatibility path
- always resolve the `NetMessageProcessor` from the native replay host instance

## Injection Ordering Findings

The injection order was wrong more than once.

### Wrong ordering #1

The saved replay path originally captured `CombatSequenceCreated` too early, before the current replay-state handler was available.

Fix applied:

- resolve the current `CombatSequenceCreated` delegate at trigger time

### Wrong ordering #2

The saved replay path then entered `ReplayState` before the native sequence handler had populated `_sequence`.

Observed symptom:

- `[ReplayState] No combat sequence found`

Fix applied:

- set `LastCombatSequence`
- sync opening `GameSim` data
- rehydrate player cards
- trigger `CombatSequenceCreated`
- only then push `ReplayState`

## Opening GameSim Handling Findings

Using `await gameSimHandler.Handle(spawnMessage)` was incorrect for saved replay bootstrap.

Why:

- it pushed the app into the normal combat-state flow
- that competed with replay startup

Fix applied:

- still validate with `processor.Handle(spawnMessage)`
- call `Data.UpdateFromGameSimAsync(spawnMessage)` directly
- mark the spawn message as handled in `GameMessageHandler` state so `ReplayState.OnCombatSequenceCreated()` can proceed

## Why Player Cards Were Missing

Native `ReplayState` does not reconstruct the player board from saved spawn events.

From decompiled behavior:

- opponent spawn events come from `_sequence.SpawnMessage.Data.Events`
- player spawn events are synthesized from `GetCardsInHand(ECombatantId.Player)`

That means a saved replay started from the lobby needs player cards to already exist in `Data.Entities`.

The original replay format did not preserve enough information for that.

## Replay Format Extensions Added

To rebuild player cards for lobby-started saved replays, the replay record was extended with `PlayerHandCards`.

Captured fields:

- `InstanceId`
- `TemplateId`
- `Type`
- `Size`
- `Section`
- `Socket`

Recording behavior:

- capture player hand cards at the opening combat `GameSim`
- tolerate capture failure and record an empty snapshot instead of breaking replay recording

Playback behavior:

- re-create player cards through `Data.GetOrCreateCard(...)`
- update from the opening `GameSim` card dictionary when available
- restore owner, section, socket, and size before `ReplayState` uses them

## Why Player Cards Still Did Not Render

After adding player card snapshots, logs showed player cards in `Cards Spawned`, but they still did not appear on screen.

Root cause:

- restored player cards had `Size = 0`
- native `AssetLoader.InstantiateItemCard(...)` throws when size is invalid

Observed log:

- `ArgumentOutOfRangeException`
- `Parameter name: size`
- `Actual value was 0`

Fix applied:

- add `Size` to `CombatReplayCardSnapshot`
- record `card.Size` during capture
- restore `card.Size = snapshot.Size` during replay rehydration

After this fix, logs showed valid player card sizes such as `Small` instead of `0`.

## Why Replay Looked "Stuck"

This was not caused by unlimited frame rate.

Native `CombatSimHandler` already throttles playback using:

- `expectedTimeMs += 50f / SpeedMultiplier`
- `await Task.Delay(...)`

Observed runtime logs:

- `Frames loaded: [321]`
- `Combat simulation completed in 16s-20s`

That is consistent with native frame pacing.

The "stuck and then effects play" feeling came from two different issues:

1. replay preparation and board setup before combat simulation starts
2. runtime exceptions in player-state and replay-end UI paths

## Current Remaining Blockers

The latest logs show that replay playback now starts and runs, but player and hero state are still incomplete.

Confirmed remaining issues:

1. `KeyNotFoundException: The given key 'HealthMax' was not present in the dictionary.`
   - source: `CombatSimHandler.ProcessPlayerUpdate(...)`
2. `NullReferenceException` in replay-end UI
   - source: `HealthBarController.ResetJoy()`
   - source: `BoardUIController.OnReplayEnded()`
3. `InvalidOperationException: Nullable object must have a value.`
   - source: `HeroLevelTooltipData.GetXpValues()`

What this means:

- bootstrap and replay startup are working
- player cards are now reconstructed
- native combat simulation is running
- the remaining failure is incomplete player/hero/UI state, especially health and hero progression data

## Recommended Next Step

Do not spend more time on replay startup ordering unless new evidence appears.

The next implementation pass should add a dedicated player-state snapshot, likely something like `CombatReplayPlayerSnapshot`, and restore it before entering `ReplayState`.

Minimum data to investigate:

- player attribute dictionary, especially `HealthMax`
- current health-related values needed by combat updates
- hero xp / level / tooltip inputs
- any replay-end UI dependencies used by `BoardUIController`

## Verification Notes

Useful log markers during validation:

- `Saved replay injection completed`
- `Started replay for saved combat`
- `[ReplayState] Replaying combat sequence`
- `Cards Spawned: [itm_... [Player] ...`
- `Frames loaded: [...]`
- `Combat simulation completed in ...`

If player cards disappear again, first check whether logs show:

- `ArgumentOutOfRangeException` with `size`
- player `Cards Spawned` lines reporting `[0]` instead of `Small` / `Medium` / `Large`
