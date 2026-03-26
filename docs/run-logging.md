# Run Logging And History

## Scope

Current run logging is a three-part feature:

- live run capture into SQLite
- in-game browsing through `HistoryPanel`
- offline export through `scripts/export_run_log.py`

Code under `Game/RunLogging/`, `Game/HistoryPanel/`, and `Game/PvpBattles/` is the source of
truth. This document describes the current implementation only.

## Key Files

- [Plugin.cs](../Plugin.cs)
- [Core/Runtime/BppRuntimeHost.cs](../Core/Runtime/BppRuntimeHost.cs)
- [Core/Paths/BppPathService.cs](../Core/Paths/BppPathService.cs)
- [Game/RunStateSyncController.cs](../Game/RunStateSyncController.cs)
- [Game/RunLifecycle/RunLifecycleModule.cs](../Game/RunLifecycle/RunLifecycleModule.cs)
- [Patches/RunLogging/RunInitializedPatch.cs](../Patches/RunLogging/RunInitializedPatch.cs)
- [Game/RunLogging/RunLoggingController.cs](../Game/RunLogging/RunLoggingController.cs)
- [Game/RunLogging/RunLoggingModule.cs](../Game/RunLogging/RunLoggingModule.cs)
- [Game/RunLogging/RunLoggingGameDataReader.cs](../Game/RunLogging/RunLoggingGameDataReader.cs)
- [Game/RunLogging/RunLogSessionManager.cs](../Game/RunLogging/RunLogSessionManager.cs)
- [Game/RunLogging/Persistence/SqliteRunLogStore.cs](../Game/RunLogging/Persistence/SqliteRunLogStore.cs)
- [Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs](../Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs)
- [Game/HistoryPanel/HistoryPanel.cs](../Game/HistoryPanel/HistoryPanel.cs)
- [Game/HistoryPanel/HistoryCollectionsEntryBridge.cs](../Game/HistoryPanel/HistoryCollectionsEntryBridge.cs)
- [Game/HistoryPanel/HistoryPanelRepository.cs](../Game/HistoryPanel/HistoryPanelRepository.cs)
- [Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs](../Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs)
- [scripts/export_run_log.py](../scripts/export_run_log.py)

## Runtime Entry

`Plugin.Awake()` attaches:

- `RunStateSyncController`
- `RunLoggingController`
- `HistoryPanel`
- `HistoryCollectionsEntryBridge`

`RunInitializedPatch` also hooks `NetMessageProcessor.ReceiveOrQueue(...)` so the runtime can
capture the authoritative server `run_id` from `NetMessageRunInitialized` before a session is
created.

## Lifecycle And Session Semantics

1. `RunLifecycleModule` keeps `BppRuntimeHost.RunContext` updated with:
   - `IsInGameRun`
   - `CurrentServerRunId`
   - `LastRunExitKind`
2. `RunStateSyncController` refreshes run state every `0.25s` and publishes
   `RunLoggingSyncRequested`.
3. `RunLoggingModule` handles that sync event. When a run is active it:
   - ensures a session exists from live game state
   - captures state snapshots
   - captures selection snapshots
   - resolves choice / selection-abandon events
4. When the run exits, `RunLoggingModule` builds a `RunLogCompletion` and finalizes the session.
   Completion is deferred briefly if replay persistence is still outstanding, so replay-linked
   battle events can land before the session closes.

`RunLogSessionManager` is responsible for session continuity:

- restoring an unfinished session from SQLite on startup
- abandoning a stale session when stored `run_id` does not match the current server `run_id`
- appending `run_started` or `run_resumed`
- saving checkpoints after accepted events

## Captured Data

The live reader uses `RunLoggingGameDataReader` plus `EncounterTracker` state to build:

- run identity and progress (`run_started`, `run_resumed`, progress snapshots)
- state snapshots (`state_seen`)
- selection snapshots (`selection_seen` and related option events)
- choice events and selection-abandon events
- `pvp_combat_recorded` events when `CombatReplayRuntime` persistence completion publishes
  `PvpBattleRecorded`

The persistence model is append-only at the event layer. Fingerprint checks suppress duplicate
`state_seen` and repeated identical selection snapshots.

## Storage

SQLite lives at:

- `<GameRoot>/BazaarPlusPlus/bazaarplusplus.db`

The current schema is defined by `RunLogSqliteSchema.BootstrapSql` and maintained by
`SqliteRunLogStore` plus `PvpBattleSqliteStore`.

High-level table roles:

- `runs`: run metadata and start state
- `run_events`: append-only event stream
- `run_checkpoints`: latest resumable snapshot
- `run_status`: terminal status row
- `pvp_battles`: persisted PVP battle manifest read side

See [reference/sqlite-schema-reference.md](./reference/sqlite-schema-reference.md) for the full
schema summary.

## History UI

`HistoryPanel` is the in-game read side for recent run data.

- Toggle key: `F8`
- Data source: `HistoryPanelRepository`
- Primary views:
  - recent runs from SQLite
  - battles linked to the selected run
  - preview rendering of stored battle snapshots through `HistoryPanelPreviewRenderer`

`HistoryCollectionsEntryBridge` adds an in-game entry point so the history panel can be opened from
the existing collections UI as well as by hotkey.

## Export

`scripts/export_run_log.py` exports one run or all runs from SQLite into JSON / NDJSON files.

Current outputs include:

- `meta.json`
- `events.ndjson`
- `decision_chain.ndjson`
- `checkpoint.json`
- `status.json`
- `pvp_battles.ndjson`

## Related References

- [reference/sqlite-schema-reference.md](./reference/sqlite-schema-reference.md)
- [reference/run-history-decompiled-analysis.md](./reference/run-history-decompiled-analysis.md)
