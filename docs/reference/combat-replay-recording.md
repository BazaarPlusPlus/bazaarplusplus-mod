# Combat Replay Recording

## Scope

This feature records a completed `PVPCombat` as the same three-message bundle the game already uses
for native combat replay:

1. opening `NetMessageGameSim`
2. `NetMessageCombatSim`
3. closing `NetMessageGameSim`

The live observer is installed by `CombatReplayCapturePatch`, which hooks
`NetMessageProcessor.ReceiveOrQueue(...)` and forwards matching messages into
`CombatReplayRuntime`.

## Storage

Replay payload files are stored under:

- `<GameRoot>/BazaarPlusPlus/CombatReplays`

Each payload file is a `*.payload.json` document containing:

- base64 MessagePack payloads for the opening game sim, combat sim, and closing game sim

Battle metadata is stored separately in SQLite as a `PvpBattleManifest` row in `pvp_battles`.
That manifest carries:

- `battle_id`
- optional `run_id`
- save time / day / hour / encounter id
- player and opponent identity
- outcome
- player / opponent item and skill snapshots used by history UI and replay bootstrap

## Replay Flow

Live capture flow:

1. `CombatReplayCaptureService` matches an opening `GameSim`, one `CombatSim`, and a closing
   `GameSim`
2. it captures player / opponent hand and skill snapshots
3. it builds:
   - `PvpReplayPayload`
   - `PvpBattleManifest`
4. `CombatReplayRuntime` enqueues asynchronous persistence through
   `CombatReplayPersistenceQueue`
5. payload and manifest persistence completion eventually publishes `PvpBattleRecorded`

Saved replay loading uses the native replay path, but with an extra rehydration step for player
cards and replay UI:

1. load the saved payload and manifest
2. deserialize the raw triplet back into `CombatSequenceMessages`
3. rebuild saved player hand cards into `Data.Entities`
4. prepare replay health bars and related board UI
5. inject the saved sequence as the current combat sequence source
6. enter `ReplayState`
7. auto-start native replay playback

This keeps playback aligned with the native replay implementation instead of rebuilding a fight from
custom logs.

Saved replay bootstrap does not call the normal run-start path. It prepares only the minimum scene
and service state needed to enter the native replay pipeline, without using
`RunManager.StartRun()` or `Events.RunStarted`.

## Replay Entry Points

Saved replay playback currently ships through two UI surfaces that share the same runtime gate:

- `HistoryPanel`: the normal history UI enables `Replay` only when the selected `pvp_battles` row
  still has a payload file and saved replay bootstrap is currently allowed
- `DebugPanel`: in debug builds, open the panel with `F2`, then switch to the `Replays` section

Available `DebugPanel` actions:

- `Replay Latest`: loads the newest saved combat
- per-entry replay buttons: load a specific saved combat

The `DebugPanel` shows the currently active replay id and the most recent saved entries. The
`HistoryPanel` gives the same replay path to non-debug users, but scoped to the currently selected
battle entry.

## Lobby Bootstrap

Saved replay playback is intentionally restricted to the lobby with no active run. Both the
`HistoryPanel` and `DebugPanel` disable replay actions while a run is in progress so the replay
bootstrap path cannot overwrite a live run state.

If replay is started from the lobby, the mod first bootstraps a minimal gameplay environment:

1. load `GameScene`
2. load `GameplayLoading`
3. initialize the board and gameplay services
4. inject the saved combat sequence
5. enter `ReplayState`

This allows saved combats to be replayed even after restarting the game.

When a replay was bootstrapped from the lobby, exiting that replay returns the game to the main menu
automatically.

`CombatReplayRuntime.Awake()` also performs orphan cleanup: it scans payload files, compares them
against the manifest catalog, and deletes payloads that no longer have a matching manifest row.

## Limitations

- Only `PVPCombat` openings create new replay candidates.
- This feature records combat replay data only, not full run timeline playback.
- Replays still depend on the runtime being able to enter the native `ReplayState`.
- Saved replay playback is blocked while an active run exists.
- Older payloads created before snapshot fields were added may still show missing player cards.
- If the underlying game changes message formats or replay initialization order, saved replays may
  stop loading until the mod is updated.
