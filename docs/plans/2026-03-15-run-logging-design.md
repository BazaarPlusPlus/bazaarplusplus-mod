# Run Logging Design

**Goal:** Persist a complete per-run history of encounter context and player choices so a run can be reconstructed later by `run_id`, with crash-safe resume, pluggable storage backends, and low operational complexity inside the BepInEx mod environment.

**Architecture:** Split the system into a storage-agnostic run logging domain and swappable persistence adapters. The logger core emits canonical run metadata, events, checkpoints, and completion records through an interface. The default adapter uses append-only per-run files on disk with a compact checkpoint snapshot for recovery. A second adapter can target SQLite without changing capture or inference logic.

**Tech Stack:** C# 12, .NET `netstandard2.1`, BepInEx, Harmony, Newtonsoft.Json, local filesystem persistence, optional SQLite adapter

## Requirements

The logger must support:

- a unique key for each run
- full per-run event history
- recovery after mod/game crash or forced close
- safe incremental writes during active gameplay
- a storage interface that supports both JSON and SQLite implementations
- later reconstruction of:
  - day/hour progression
  - encounter/options shown
  - inferred player choices

The first version does **not** need:

- a relational query engine
- perfect inference for every choice edge case
- cross-run analytics
- UI for browsing logs

## Design Options

### Option 1: Single monolithic JSON file

Store all runs inside one `runs.json`, rewriting the file whenever state changes.

Pros:

- simple shape
- easy to inspect for tiny datasets

Cons:

- fragile if the game crashes mid-write
- expensive full-file rewrites
- poor fit for append-heavy event logging
- awkward recovery and dedupe

Decision:

- reject

### Option 2: SQLite-backed run/event store

Store `runs`, `events`, and `checkpoints` in a local SQLite database.

Pros:

- strong transactional guarantees
- powerful later querying
- clean uniqueness constraints

Cons:

- higher implementation complexity
- more moving parts in the mod runtime
- less transparent for manual inspection/debugging

Decision:

- defer
- keep as a future upgrade path if querying becomes a real need

### Option 3: Per-run directory with NDJSON event log and checkpoint

Store each run in its own directory with append-only `events.ndjson` and a small `checkpoint.json`.

Pros:

- crash-tolerant append model
- easy manual inspection
- simple recovery path
- easy to migrate later into SQLite if needed

Cons:

- query power is limited compared with a database
- requires explicit dedupe logic

Decision:

- **recommended**

### Storage abstraction decision

Independently of backend choice, the domain layer should write only through a persistence interface.

Decision:

- **required**
- JSON file storage is the default adapter
- SQLite is a peer adapter behind the same interface

## Persistence Abstraction

The capture pipeline must not know whether data is stored in files or SQLite.

### Core rule

Everything below should depend on an interface, not a file path or SQL statement:

- run lifecycle creation
- event append
- checkpoint save/load
- completion save
- active run recovery

### Recommended interfaces

```csharp
public interface IRunLogStore
{
    RunLogSessionState? TryResumeActiveRun();
    RunLogSessionState CreateRun(RunLogCreateRequest request);
    void AppendEvent(string runId, RunLogEvent entry);
    void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint);
    void CompleteRun(string runId, RunLogCompletion completion);
    void MarkRunAbandoned(string runId, RunLogAbandonment abandonment);
}
```

Optional narrower interfaces if separation becomes useful:

```csharp
public interface IRunLogReader
{
    RunLogSessionState? TryResumeActiveRun();
}

public interface IRunLogWriter
{
    RunLogSessionState CreateRun(RunLogCreateRequest request);
    void AppendEvent(string runId, RunLogEvent entry);
    void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint);
    void CompleteRun(string runId, RunLogCompletion completion);
    void MarkRunAbandoned(string runId, RunLogAbandonment abandonment);
}
```

### Canonical persistence models

The interface should operate on canonical storage DTOs rather than backend-specific shapes:

- `RunLogCreateRequest`
- `RunLogSessionState`
- `RunLogEvent`
- `RunLogCheckpoint`
- `RunLogCompletion`
- `RunLogAbandonment`

These models define the persisted contract. JSON and SQLite adapters both map from the same canonical types.

### Adapters

Recommended first two adapters:

- `JsonRunLogStore`
- `SqliteRunLogStore`

The logger core should receive `IRunLogStore` via construction or initialization and should not branch on backend type.

## Runtime Component Boundaries

Recommended service split:

- `RunLogCaptureService`
  - listens to runtime/game message sources
  - converts raw game state into canonical domain events
- `RunLogInferenceService`
  - derives `choice_made` and dedupe keys
- `RunLogSessionManager`
  - owns active in-memory run session state
  - coordinates create/resume/complete
- `IRunLogStore`
  - persistence boundary

This keeps file and SQL concerns out of event capture logic.

## Recommended Default Storage Layout

This section describes the JSON adapter only.

Base path:

`BepInEx/config/BazaarPlusPlus/run-logs/`

Layout:

```text
run-logs/
  runs-index.json
  active-run.json
  2026-03-15/
    run_20260315T121530Z_vanessa_ranked_a1b2c3d4/
      meta.json
      events.ndjson
      checkpoint.json
      status.json
```

File roles:

- `runs-index.json`
  - optional lightweight index of known runs for later discovery
- `active-run.json`
  - pointer to the currently active run and last known file paths
- `meta.json`
  - immutable or rarely changed run identity fields
- `events.ndjson`
  - append-only event stream, one JSON object per line
- `checkpoint.json`
  - latest durable recovery state
- `status.json`
  - final completion/interruption summary

The SQLite adapter stores the same logical content in tables instead of files.

Suggested tables:

- `runs`
- `run_events`
- `run_checkpoints`
- `run_status`
- `active_run`

## JSON Adapter Schema

The JSON adapter should be treated as the reference wire/storage format for v1.

### Design rules

- one `run_id` maps to one directory
- small stable metadata goes into `meta.json`
- append-only history goes into `events.ndjson`
- mutable recovery state goes into `checkpoint.json`
- terminal summary goes into `status.json`
- every file should include `schema_version`

### Why not one big JSON file per run

Reject:

- `run.json` with embedded `events: []`

Reasons:

- each append would require a read-modify-write cycle
- crash windows are larger
- partial corruption costs more
- tail recovery is worse than NDJSON

The directory-with-multiple-files shape is the intended JSON design.

### File 1: `meta.json`

Purpose:

- immutable or rarely changing run identity

Suggested schema:

```json
{
  "schema_version": 1,
  "run_id": "run_20260315T121530Z_vanessa_ranked_a1b2c3d4",
  "created_at_utc": "2026-03-15T12:15:30Z",
  "started_at_utc": "2026-03-15T12:15:31Z",
  "hero": "Vanessa",
  "game_mode": "Ranked",
  "data_version": "0.1.2",
  "status": "active",
  "source": {
    "kind": "generated",
    "session_nonce": "4c23f1f6",
    "identity_seed": "2026-03-15T12:15:31Z|Vanessa|Ranked|1|1|4c23f1f6"
  }
}
```

Field guidance:

- `schema_version`
  - required
- `run_id`
  - required
- `created_at_utc`
  - required
- `started_at_utc`
  - required once the run is truly active
- `hero`
  - required if known
- `game_mode`
  - required if known
- `data_version`
  - optional but preferred
- `status`
  - `active`, `completed`, `abandoned`, `interrupted`
- `source`
  - useful for explaining how `run_id` was derived

### File 2: `events.ndjson`

Purpose:

- the durable event ledger for the run

Encoding:

- UTF-8
- one JSON object per line
- no wrapping array
- append-only

Common event envelope:

```json
{
  "schema_version": 1,
  "run_id": "run_20260315T121530Z_vanessa_ranked_a1b2c3d4",
  "seq": 42,
  "ts": "2026-03-15T12:48:02.120Z",
  "kind": "selection_seen",
  "day": 3,
  "hour": 2,
  "state": "Encounter"
}
```

Common fields:

- `schema_version`
  - required
- `run_id`
  - required
- `seq`
  - required, strictly increasing per run
- `ts`
  - required UTC timestamp
- `kind`
  - required event type
- `day`
  - optional for some events, required for most run events
- `hour`
  - optional for some events, required for most run events
- `state`
  - optional for progress events, required for state/selection events

#### Event: `run_started`

```json
{
  "schema_version": 1,
  "run_id": "run_...",
  "seq": 1,
  "ts": "2026-03-15T12:15:31.000Z",
  "kind": "run_started",
  "day": 1,
  "hour": 1,
  "hero": "Vanessa",
  "game_mode": "Ranked"
}
```

#### Event: `run_progress`

```json
{
  "schema_version": 1,
  "run_id": "run_...",
  "seq": 7,
  "ts": "2026-03-15T12:20:10.000Z",
  "kind": "run_progress",
  "day": 1,
  "hour": 2,
  "victories": 0,
  "losses": 0,
  "current_hour_xp": 2
}
```

#### Event: `state_seen`

```json
{
  "schema_version": 1,
  "run_id": "run_...",
  "seq": 11,
  "ts": "2026-03-15T12:21:05.000Z",
  "kind": "state_seen",
  "day": 1,
  "hour": 2,
  "state": "Encounter",
  "encounter_id": "df2d7d1a-...",
  "reroll_cost": null,
  "rerolls_remaining": null,
  "state_fingerprint": "sha1(...)"
}
```

#### Event: `selection_seen`

```json
{
  "schema_version": 1,
  "run_id": "run_...",
  "seq": 12,
  "ts": "2026-03-15T12:21:05.300Z",
  "kind": "selection_seen",
  "day": 1,
  "hour": 2,
  "state": "Encounter",
  "encounter_id": "df2d7d1a-...",
  "selection_fingerprint": "sha1(...)",
  "selection_context_rules": {
    "raw": {}
  },
  "options": [
    {
      "index": 0,
      "instance_id": "card_inst_1",
      "template_id": "templ_1",
      "name": "Frost Street",
      "tier": "Bronze",
      "enchant": "None",
      "tags": ["Encounter"],
      "attributes": {
        "Health": 20
      }
    }
  ]
}
```

`options[]` should be the richest stable snapshot we can cheaply build at capture time.

Recommended option fields:

- `index`
- `instance_id`
- `template_id`
- `name`
- `tier`
- `enchant`
- `tags`
- `attributes`

Optional later fields:

- `card_type`
- `owner`
- `section`
- `left_socket_id`

#### Event: `choice_made`

```json
{
  "schema_version": 1,
  "run_id": "run_...",
  "seq": 13,
  "ts": "2026-03-15T12:21:07.000Z",
  "kind": "choice_made",
  "day": 1,
  "hour": 2,
  "state": "Encounter",
  "encounter_id": "df2d7d1a-...",
  "selection_seq": 12,
  "selected_instance_id": "card_inst_1",
  "selected_template_id": "templ_1",
  "selected_name": "Frost Street",
  "inferred_from": "state_transition",
  "confidence": "high"
}
```

If inference is weak:

- keep `selection_seq`
- allow selected fields to be `null`
- set `confidence` to `low`

#### Event: `run_completed`

```json
{
  "schema_version": 1,
  "run_id": "run_...",
  "seq": 88,
  "ts": "2026-03-15T14:20:10.000Z",
  "kind": "run_completed",
  "day": 10,
  "hour": 1,
  "victories": 8,
  "losses": 2,
  "outcome": "completed"
}
```

### File 3: `checkpoint.json`

Purpose:

- resume active write session
- restore dedupe anchors
- avoid replaying the full event file during recovery

Suggested schema:

```json
{
  "schema_version": 1,
  "run_id": "run_20260315T121530Z_vanessa_ranked_a1b2c3d4",
  "last_seq": 128,
  "last_seen_at_utc": "2026-03-15T13:02:11Z",
  "day": 4,
  "hour": 2,
  "state": "Choice",
  "current_encounter_id": "df2d7d1a-...",
  "last_state_fingerprint": "sha1(...)",
  "last_selection_fingerprint": "sha1(...)",
  "pending_selection_seq": 126,
  "completed": false
}
```

Field guidance:

- `last_seq`
  - last durable event sequence
- `last_seen_at_utc`
  - last durable write timestamp
- `day`, `hour`, `state`
  - latest known run position
- `current_encounter_id`
  - latest known encounter
- `last_state_fingerprint`
  - dedupe anchor for `state_seen`
- `last_selection_fingerprint`
  - dedupe anchor for `selection_seen`
- `pending_selection_seq`
  - helps attach later `choice_made`
- `completed`
  - terminal flag

### File 4: `status.json`

Purpose:

- terminal run summary
- quick read without replaying the event stream

Suggested schema:

```json
{
  "schema_version": 1,
  "run_id": "run_20260315T121530Z_vanessa_ranked_a1b2c3d4",
  "status": "completed",
  "ended_at_utc": "2026-03-15T14:20:10Z",
  "final_day": 10,
  "final_hour": 1,
  "victories": 8,
  "losses": 2,
  "reason": "run_end_event"
}
```

Allowed terminal `status` values:

- `completed`
- `interrupted`
- `abandoned`

### Root helper file: `active-run.json`

Purpose:

- locate the current active run on startup

Suggested schema:

```json
{
  "schema_version": 1,
  "run_id": "run_20260315T121530Z_vanessa_ranked_a1b2c3d4",
  "date_partition": "2026-03-15",
  "run_path": "2026-03-15/run_20260315T121530Z_vanessa_ranked_a1b2c3d4",
  "last_seq": 128,
  "updated_at_utc": "2026-03-15T13:02:11Z"
}
```

This file is only for resume discovery and should not be treated as the source of truth for run contents.

### Root helper file: `runs-index.json`

Purpose:

- optional lightweight discovery index for browsing later

Suggested schema:

```json
{
  "schema_version": 1,
  "runs": [
    {
      "run_id": "run_20260315T121530Z_vanessa_ranked_a1b2c3d4",
      "date_partition": "2026-03-15",
      "hero": "Vanessa",
      "game_mode": "Ranked",
      "status": "completed",
      "started_at_utc": "2026-03-15T12:15:31Z",
      "ended_at_utc": "2026-03-15T14:20:10Z"
    }
  ]
}
```

This file is convenience-only. If it goes missing or stale, run data should still be recoverable from per-run directories.

## Run Identity

### `run_id`

The logger should use one stable `run_id` for the entire run lifetime.

Recommended format:

`run_<start_utc>_<hero>_<mode>_<short_hash>`

Example:

`run_20260315T121530Z_vanessa_ranked_a1b2c3d4`

### First-version generation strategy

Until the decompiled runtime reveals a truly stable server-side run GUID, generate `run_id` from:

- first confirmed run start UTC
- hero
- play mode
- first seen day/hour
- random session nonce

Then hash the composite string and keep a short suffix for readability.

### Stability rule

Once a run has been assigned a `run_id`, never replace it during recovery. If the mod restarts mid-run, resume using the existing `run_id` from `active-run.json`.

## Core Data Model

### Canonical run metadata

Purpose:

- identify the run
- hold fields that should not be duplicated on every event

Suggested shape:

```json
{
  "run_id": "run_20260315T121530Z_vanessa_ranked_a1b2c3d4",
  "created_at_utc": "2026-03-15T12:15:30Z",
  "started_at_utc": "2026-03-15T12:15:31Z",
  "hero": "Vanessa",
  "game_mode": "Ranked",
  "data_version": "x.y.z",
  "status": "active"
}
```

For the JSON adapter this is stored in `meta.json`.

For the SQLite adapter this lives in the `runs` table.

### Canonical event stream

Purpose:

- durable append-only run history
- source of truth for reconstruction

Each line contains:

- `seq`
- `ts`
- `kind`
- run progress fields as relevant
- event-specific payload

Example:

```json
{"seq":1,"ts":"2026-03-15T12:15:31Z","kind":"run_started","day":1,"hour":1}
{"seq":2,"ts":"2026-03-15T12:16:02Z","kind":"selection_seen","day":1,"hour":1,"state":"Encounter","encounter_id":"...","selection_fingerprint":"...","options":[{"instance_id":"...","template_id":"...","name":"..."}]}
{"seq":3,"ts":"2026-03-15T12:16:05Z","kind":"choice_made","day":1,"hour":1,"state":"Encounter","selection_seq":2,"selected_instance_id":"...","selected_template_id":"...","inferred_from":"state_transition"}
```

For the JSON adapter this is stored in `events.ndjson`.

For the SQLite adapter this is stored in `run_events`.

### Canonical checkpoint

Purpose:

- fast recovery without replaying the full file on every update
- dedupe anchor after restart

Suggested shape:

```json
{
  "run_id": "run_20260315T121530Z_vanessa_ranked_a1b2c3d4",
  "last_seq": 128,
  "last_seen_at_utc": "2026-03-15T13:02:11Z",
  "day": 4,
  "hour": 2,
  "state": "Choice",
  "current_encounter_id": "...",
  "last_selection_fingerprint": "sha1(...)",
  "completed": false
}
```

For the JSON adapter this is stored in `checkpoint.json`.

For the SQLite adapter this is stored in `run_checkpoints`.

### Canonical completion status

Purpose:

- mark final run outcome without replaying the whole event stream

Suggested shape:

```json
{
  "run_id": "...",
  "status": "completed",
  "ended_at_utc": "2026-03-15T14:20:10Z",
  "final_day": 10,
  "victories": 8,
  "losses": 2
}
```

## Event Types

The first version should limit itself to a small, high-value event set.

### 1. `run_started`

Emitted when a run is first confirmed active.

Fields:

- `day`
- `hour`
- `hero`
- `game_mode`

### 2. `run_progress`

Emitted when day/hour/win-loss changes and there is no richer state event to cover it.

Fields:

- `day`
- `hour`
- `victories`
- `losses`
- `current_hour_xp` if available from raw message

### 3. `state_seen`

Emitted when the current run state changes or when a new state snapshot first becomes visible.

Fields:

- `state`
- `day`
- `hour`
- `encounter_id`
- `reroll_cost`
- `rerolls_remaining`

### 4. `selection_seen`

The most important capture event. Records the options shown to the player at a specific state.

Fields:

- `day`
- `hour`
- `state`
- `encounter_id`
- `selection_fingerprint`
- `selection_context_rules`
- `options[]`

Each option should include at least:

- `instance_id`
- `template_id`
- `name`
- `tier`
- `tags`
- `enchant`
- `attributes`

### 5. `choice_made`

Records the selected option when the logger can infer it.

Fields:

- `day`
- `hour`
- `state`
- `encounter_id`
- `selection_seq`
- `selected_instance_id`
- `selected_template_id`
- `selected_name`
- `inferred_from`
- `confidence`

If no reliable inference is possible, the logger should still preserve `selection_seen` and may skip `choice_made` or emit it with `selected_* = null` and `confidence = "low"`.

### 6. `run_completed`

Emitted when the run ends normally or is interrupted.

Fields:

- `final_day`
- `victories`
- `losses`
- `outcome`

For the JSON adapter this is stored in `status.json`.

For the SQLite adapter this is stored in `run_status`.

## Capture Sources

### Primary inputs

The logger should read from these decompiled/runtime surfaces:

1. `NetMessageGameSim.Data.Run`
2. `NetMessageGameSim.Data.CurrentState`
3. `GameSimEventStateTransitioned`
4. `GameSimEventStateSuspended`
5. `GameSimEventStateResumed`
6. runtime entity lookup for `SelectionSet` resolution

### Why these are enough for v1

These inputs already provide:

- current day/hour
- current encounter id
- current selection set ids
- state transitions

That is sufficient to build a good live event log even though the game does not preserve a detailed history for us.

## Choice Inference Strategy

Choice inference should be incremental and conservative.

### Tier 1: Direct event mapping

If later decompiled analysis reveals a direct event that clearly identifies the chosen selection item, use it directly.

### Tier 2: State transition inference

When a selection state disappears:

- compare the previous `selection_seen`
- compare current entities and inventory
- compare current encounter/state
- infer which option resolved into the next state or entered the player state

Use this for:

- `Encounter`
- `Choice`
- `Loot`
- `Pedestal`

### Tier 3: Unknown-safe fallback

If the choice cannot be inferred with enough confidence:

- keep `selection_seen`
- do not invent a precise `choice_made`
- optionally emit a low-confidence placeholder

This prevents silent data loss while avoiding false certainty.

## Dedupe Strategy

Because the game may resend equivalent state or the mod may restart mid-state, dedupe is required.

### Selection fingerprint

Generate:

`sha1(day + hour + state + encounter_id + sorted(selection instance ids))`

Use it to suppress duplicate `selection_seen` writes for the same visible choice set.

### Event identity rules

- `run_started`
  - only once per `run_id`
- `selection_seen`
  - unique by `selection_fingerprint`
- `state_seen`
  - unique by `(day, hour, state, encounter_id, seq-window)`
- `choice_made`
  - unique by `(selection_seq, selected_instance_id)`

## Resume / Crash Recovery

### Recovery contract

On startup:

1. call `IRunLogStore.TryResumeActiveRun()`
2. if it returns an unfinished run, reopen that run
3. restore checkpoint and dedupe anchors from the returned session state
4. rebuild any needed in-memory tail cache

### Recovery write model

For each accepted event:

1. call `AppendEvent`
2. ensure the backend durably commits the event
3. call `SaveCheckpoint`
4. ensure active-run resume state is updated

This ordering ensures:

- no checkpoint points past a missing event
- after a crash, replay can start from the last durable sequence

### Stale active run handling

If an unfinished active run exists but the current runtime clearly started a new run:

- mark the old run as `abandoned` or `interrupted`
- close it cleanly
- allocate a new `run_id`

## In-Memory Runtime State

Use a small in-memory session object while the run is active:

- `RunLoggingSession`
  - `RunId`
  - `LastSeq`
  - `LastDay`
  - `LastHour`
  - `LastState`
  - `LastEncounterId`
  - `LastSelectionFingerprint`
  - `PendingSelection`
  - dedupe sets for recent event keys

This state should be reconstructable from disk after restart.

## Backend Semantics

The interface contract should define backend guarantees so JSON and SQLite behave equivalently at the domain level.

Required guarantees:

- `CreateRun` is idempotent for an already-resumed active run
- `AppendEvent` preserves ordering by `seq`
- `SaveCheckpoint` never advances past a missing durable event
- `CompleteRun` is terminal
- `TryResumeActiveRun` returns enough state to continue dedupe and sequencing safely

### JSON adapter semantics

- append to `events.ndjson`
- flush after each event or tiny batch
- write checkpoint/status atomically via temp file replacement
- maintain `active-run.json`

### SQLite adapter semantics

- use one transaction for event append plus active-run update when appropriate
- persist checkpoint in the same or a strictly later transaction than the corresponding event append
- enforce uniqueness on `run_id`, `seq`, and any dedupe keys worth indexing

## Write Safety

Recommended write behavior for the default JSON adapter:

- open `events.ndjson` once for append during active session
- flush after each event or after very small batches
- write `checkpoint.json` atomically via temp file + replace
- write `status.json` atomically on completion

The design should optimize for correctness over write throughput.

## Versioning

Add a schema version to every persisted file family.

Recommended field:

- `schema_version: 1`

This will matter once event payloads evolve.

## Proposed Implementation Order

### Phase 1: Persistence skeleton

Create:

- run directory creation
- `run_id` generation
- append writer
- checkpoint writer
- active run pointer

### Phase 2: Live run progress capture

Capture:

- `run_started`
- `run_progress`
- `state_seen`

### Phase 3: Selection capture

Capture:

- `selection_seen`
- resolved option details from `SelectionSet`

### Phase 4: Choice inference

Infer and write:

- `choice_made`

Start with supported states:

- `Encounter`
- `Choice`
- `Loot`
- `Pedestal`

### Phase 5: Recovery hardening

Add:

- dedupe restoration
- stale run handling
- interrupted/abandoned finalization

## Risks

### 1. No true server run GUID yet

Mitigation:

- use a generated stable local `run_id`
- isolate ID generation logic so it can be replaced later

### 2. Choice inference may be ambiguous

Mitigation:

- keep `selection_seen` as the durable minimum truth
- record inference confidence explicitly

### 3. Duplicate writes around state replay/recovery

Mitigation:

- fingerprint selection payloads
- restore dedupe anchors from checkpoint and event tail

### 4. Mid-write crash

Mitigation:

- append-only event stream
- atomic checkpoint/status file replacement

## Recommendation

Build the first version as:

- a storage-agnostic run logging core behind `IRunLogStore`
- `JsonRunLogStore` as the default backend
- `SqliteRunLogStore` as a planned peer backend
- one directory per run for the JSON backend
- append-only `events.ndjson`
- atomic `checkpoint.json`
- generated stable `run_id`
- mandatory `selection_seen`
- conservative `choice_made` inference

This gives the best balance of:

- recoverability
- implementation cost
- debuggability
- future migration flexibility
- backend swap safety

## Follow-up

If this design is approved, the next step should be a concrete implementation plan covering:

- runtime hooks to capture `GameSim` updates
- storage interfaces and canonical persistence DTOs
- JSON adapter behavior
- SQLite adapter schema
- persistence service boundaries
- serialization types
- recovery tests
- choice inference tests
