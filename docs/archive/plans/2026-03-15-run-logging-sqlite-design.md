# Run Logging SQLite-Only Design

**Goal:** Make SQLite the single runtime storage backend for run logging, while keeping JSON available only as an offline export/debug format.

**Architecture:** Keep the existing storage-agnostic run logging domain in C#, but reduce runtime persistence to a single `SqliteRunLogStore` implementation behind `IRunLogStore`. Remove JSON file persistence from the mod runtime entirely. Add a separate Python export tool that reads canonical run data from SQLite and emits the current JSON debug layout on demand.

**Tech Stack:** C# 12, .NET `netstandard2.1`, BepInEx, Harmony, `Microsoft.Data.Sqlite`, SQLite, Python 3, JSON export scripts

## Requirements

The SQLite-only runtime must support:

- a unique `run_id` per run
- append-only event history
- crash-safe active-run recovery
- durable checkpoints
- terminal completion and abandonment records
- no runtime dependency on JSON helper files
- no capture/session/inference branching on backend type

The export tool must support:

- exporting one `run_id` into the existing JSON debug layout
- exporting all runs in the database in one command
- emitting:
  - `meta.json`
  - `events.ndjson`
  - `checkpoint.json`
  - `status.json`
- preserving the same field names and general shapes that current debugging expects

The new design explicitly does **not** need:

- runtime dual-write to JSON and SQLite
- live JSON mirrors for inspection
- analytics queries beyond recovery/export needs
- a user-facing in-game log browser

## Decision Summary

### Why not keep runtime JSON alongside SQLite

Running JSON and SQLite in parallel weakens consistency rather than improving it:

- a crash can leave one backend ahead of the other
- recovery logic becomes duplicated
- debugging becomes ambiguous because there are two possible sources of truth
- dedupe and sequence guarantees must be kept in sync across two persistence paths

Decision:

- reject runtime dual-write

### Why SQLite becomes the single runtime source of truth

SQLite is the better final runtime backend because it gives:

- transactional updates
- unique constraints for sequencing
- a simpler single-source recovery path
- better performance and future query options

Decision:

- **recommended**
- SQLite is the only runtime backend
- JSON is downgraded to an offline export format

## Runtime Boundaries

The domain layer remains the same:

- `IRunLogStore`
- `RunLogCreateRequest`
- `RunLogSessionState`
- `RunLogEvent`
- `RunLogCheckpoint`
- `RunLogCompletion`
- `RunLogAbandonment`
- `RunLogSessionManager`
- `RunLogCaptureService`
- `RunLogInferenceService`

The important rule is unchanged:

- capture/session/inference operate on canonical DTOs only
- SQL statements live only inside `SqliteRunLogStore`
- controller logic must not know table names or SQL shape

This keeps runtime code maintainable while avoiding a storage abstraction that is so thin it becomes fake.

## SQLite Schema

The runtime database should live under the BazaarPlusPlus config area, for example:

`BepInEx/config/BazaarPlusPlus/run-logs.db`

Recommended tables:

### `runs`

Purpose:

- one row per run
- stable identity and start metadata

Recommended columns:

- `run_id TEXT PRIMARY KEY`
- `schema_version INTEGER NOT NULL`
- `started_at_utc TEXT NOT NULL`
- `hero TEXT NOT NULL`
- `game_mode TEXT NOT NULL`
- `day INTEGER NULL`
- `hour INTEGER NULL`
- `seed INTEGER NULL`
- `status TEXT NOT NULL`

### `run_events`

Purpose:

- append-only event stream
- source of truth for reconstruction

Recommended columns:

- `run_id TEXT NOT NULL`
- `seq INTEGER NOT NULL`
- `ts_utc TEXT NOT NULL`
- `kind TEXT NOT NULL`
- `payload_json TEXT NOT NULL`

Recommended constraints:

- `PRIMARY KEY (run_id, seq)`
- `FOREIGN KEY (run_id) REFERENCES runs(run_id)`

`payload_json` stores the canonical event JSON envelope so export stays straightforward and runtime DTO mapping remains stable.

### `run_checkpoints`

Purpose:

- one current recovery snapshot per run
- fast resume without replaying the entire event stream

Recommended columns:

- `run_id TEXT PRIMARY KEY`
- `schema_version INTEGER NOT NULL`
- `last_seq INTEGER NOT NULL`
- `last_seen_at_utc TEXT NOT NULL`
- `day INTEGER NULL`
- `hour INTEGER NULL`
- `state TEXT NULL`
- `current_encounter_id TEXT NULL`
- `last_state_fingerprint TEXT NULL`
- `last_selection_fingerprint TEXT NULL`
- `pending_selection_seq INTEGER NULL`
- `completed INTEGER NOT NULL`

### `run_status`

Purpose:

- terminal summary
- quick completion lookup

Recommended columns:

- `run_id TEXT PRIMARY KEY`
- `schema_version INTEGER NOT NULL`
- `status TEXT NOT NULL`
- `ended_at_utc TEXT NOT NULL`
- `final_day INTEGER NULL`
- `final_hour INTEGER NULL`
- `victories INTEGER NULL`
- `losses INTEGER NULL`
- `reason TEXT NULL`

## Runtime Recovery Semantics

Runtime resume should no longer depend on `active-run.json`.

Recommended rule:

1. query for runs with no row in `run_status`
2. join with checkpoint state
3. if multiple unfinished runs somehow exist, prefer the one with the newest `last_seen_at_utc`
4. if state is inconsistent, fail closed and return no resumable session rather than inventing one

This keeps recovery conservative and auditable.

## SQLite Best Practices For This Codebase

To keep the C# side maintainable:

- use parameterized SQL everywhere
- centralize schema creation/migration inside the store
- centralize DTO <-> row mapping helpers
- wrap multi-step writes in transactions
- keep SQL text close to the store, not scattered across controllers
- use explicit indexes and uniqueness constraints rather than in-memory assumptions
- store timestamps in UTC ISO 8601 text consistently
- store canonical event payloads as JSON text, not ad hoc column explosions

The store should be the only place that knows:

- database path
- table names
- schema version/migration details
- SQL text

## JSON Export Tool

The Python export tool should:

- accept a SQLite database path and either `run_id` or `--all`
- load the run, event stream, checkpoint, and terminal status
- write a directory shaped like the old JSON debug layout

Suggested output layout:

```text
<output-root>/
  <date-partition>/
    <run_id>/
      meta.json
      events.ndjson
      checkpoint.json
      status.json
```

This tool is for inspection only. It does not participate in runtime writes or recovery.

## Migration / Cleanup Plan

The runtime JSON persistence code should be removed from C# once SQLite is in place:

- remove `JsonRunLogStore`
- remove JSON path layout/file helper classes that exist only for runtime persistence
- remove controller wiring that selects JSON as the active runtime store
- remove JSON runtime tests and replace them with SQLite tests plus export tests

The canonical DTOs and `RunIdFactory` should stay because they are runtime-domain concerns, not JSON-specific concerns.

## Validation Strategy

The new implementation is correct only if all three layers are verified:

1. SQLite store contract tests
2. runtime controller/session seam tests against SQLite
3. Python export tests proving JSON output shape remains compatible

The main correctness bar is:

- SQLite is the only runtime source of truth
- export reads from SQLite only
- the runtime no longer depends on JSON files for correctness
