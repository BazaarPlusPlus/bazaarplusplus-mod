# Combat Replay Recording

## Scope

This feature records a completed combat as the same three-message bundle the game already uses for native combat replay:

1. opening `NetMessageGameSim`
2. `NetMessageCombatSim`
3. closing `NetMessageGameSim`

The recorder hooks `NetMessageProcessor.ReceiveOrQueue(...)`, watches the live message stream, and persists only valid combat triplets.

## Storage

Replay files are stored under:

- `BepInEx/config/BazaarPlusPlus/CombatReplays`

Each replay is written as a JSON file containing:

- metadata: replay id, run id, day, hour, encounter id, opponent name, save time
- player hand snapshot data used to rebuild player cards for lobby-started replays
- base64 MessagePack payloads for the opening game sim, combat sim, and closing game sim

## Replay Flow

Saved replay loading uses the game's existing combat replay path, but now includes an extra rehydration step for player cards:

1. load the saved triplet from disk
2. deserialize it back into `CombatSequenceMessages`
3. rebuild player hand cards into `Data.Entities`
4. inject the saved sequence as the current combat sequence source
5. sync the saved opening `GameSim`
6. enter `ReplayState`
7. start native replay playback

This keeps combat playback aligned with the native replay implementation instead of rebuilding the fight from custom logs.

Saved replay bootstrap does not call the normal run-start path. It prepares only the minimum gameplay scene state needed to enter the native replay pipeline, without using `RunManager.StartRun()` or `Events.RunStarted`.

## Debug Panel

In debug builds, open the panel with `F2`, then switch to the `Replays` section.

Available actions:

- `Replay Latest`: loads the newest saved combat
- per-entry replay buttons: load a specific saved combat

The panel shows the currently active replay id and the most recent saved entries.

## Lobby Bootstrap

Saved replay playback is intentionally restricted to the lobby with no active run. The debug panel disables replay actions while a run is in progress so the recorded spawn snapshot cannot overwrite live run state.

If replay is started from the lobby, the mod first bootstraps a minimal gameplay environment:

1. load `GameScene`
2. load `GameplayLoading`
3. initialize the board and gameplay services
4. inject the saved combat sequence
5. enter `ReplayState`

This allows saved combats to be replayed even after restarting the game.

When a replay was bootstrapped from the lobby, exiting that replay returns the game to the main menu automatically.

Verification for this flow is based on local runtime logs plus code-path review. The expected checkpoints are bootstrap start, scene readiness, dependency resolution, saved sequence injection, `ReplayState` entry, native replay start, and rollback or menu-return handling when applicable.

## Limitations

- This feature records combat replay data only, not full run timeline playback.
- Replays depend on the runtime being able to enter the native `ReplayState`.
- Saved replay playback is blocked while an active run exists.
- Older replay files created before player-card snapshot fields were added may still fail to show player cards correctly.
- Current known blocker: player and hero state is still incomplete during replay playback, causing runtime errors around `HealthMax`, hero tooltip data, and replay-end UI reset.
- If the underlying game changes message formats or replay initialization order, saved replays may stop loading until the mod is updated.
