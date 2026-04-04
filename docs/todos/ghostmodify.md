# Ghost Battle Perspective Cleanup Design

## Background

Ghost Panel currently presents battles from the local player's perspective, but the
`GET /players/me/ghost-battles` API returns battle rows from the remote uploader's
perspective:

- `player_*` identifies the uploader
- `opponent_*` identifies the current local player
- `winner_combatant_id = Player` means the uploader won
- `winner_combatant_id = Opponent` means the local player won

The current client implementation flips these fields during import and then stores the
flipped values in local SQLite. This makes the UI easy to render, but it also means the
same field names have different meanings at different layers.

## Problem

The current design mixes raw facts with presentation perspective.

That causes several recurring issues:

- Service responses, local SQLite rows, and UI records do not share a single meaning
- Debugging requires remembering whether a value has already been flipped
- `Player` and `Opponent` can be interpreted incorrectly when logic is added later
- Filtering, labels, and tests can silently drift away from the actual source-of-truth

The recent `I Won / I Lost` reversal bug is an example of this failure mode. The UI
needs local-player semantics, but the storage layer should not have to rewrite the
meaning of raw remote data to provide that view.

## Goal

Keep the Ghost Panel experience in local-player perspective while removing perspective
rewrites from the persistence path.

The intended end state is:

- remote ghost battle responses are stored locally using their original server semantics
- local-player perspective is derived explicitly by one projection layer
- UI rendering, filtering, and battle summary formatting all consume that projection
- raw storage remains easy to compare against server responses during debugging

## Non-Goals

This design does not require:

- changing the server API contract
- changing replay payload or manifest formats
- changing replay-link authorization
- changing the user-facing Ghost Panel layout or terminology

## Proposed Design

Split ghost battle handling into two distinct layers.

### 1. Raw Ghost Battle Storage

This layer stores and returns the server response without perspective translation.

Responsibilities:

- persist remote `player_*`, `opponent_*`, `result`, and `winner_combatant_id` exactly as
  returned by `/players/me/ghost-battles`
- keep sync, retention, replay availability, and replay-downloaded state attached to the
  raw battle row
- make local SQLite rows easy to compare with server payloads and tests

Key principle:

- the repository stores facts, not a UI-specific interpretation of those facts

### 2. Local Perspective Projection

This layer converts a raw against-me ghost battle into the local player's perspective
only when the History Panel needs to display or filter it.

Responsibilities:

- derive `YOU` and `OPP` participant roles
- derive local `Win / Loss / Unknown`
- produce the names, heroes, and outcome fields the UI expects
- provide a single source of truth for `I Won / I Lost` filtering

Suggested shape:

- `GhostBattlePerspectiveAdapter`
- `GhostBattleLocalViewModel`
- `ToLocalPerspective(...)`

The important part is not the exact type name. The important part is that the
translation lives in one place and every ghost-specific UI path uses it.

## Suggested Data Flow

The ghost battle path should look like this:

1. Server returns against-me ghost battle rows using uploader perspective
2. Client stores those rows in SQLite unchanged
3. History Panel reads raw ghost battle rows from the repository
4. A dedicated projection layer converts each raw row into local-player perspective
5. UI rendering, labels, colors, and filters consume the projected view model

This gives each layer a clear responsibility:

- transport returns raw API data
- repository stores raw API data
- projection adapts raw API data for local-player display
- UI consumes display-ready data

## Why This Is Better

### Clearer Semantics

The same field means the same thing in the server response, repository row, and sync
tests. There is no hidden semantic rewrite during import.

### Easier Debugging

A bug can be isolated more quickly:

- if the raw row is wrong, the sync or server query is wrong
- if the raw row is correct but the UI is wrong, the projection is wrong

That is much easier to reason about than a system where the raw row is already a
transformed version of the response.

### Safer UI Logic

Ghost filtering, summary labels, and participant display no longer need to re-interpret
`Player` and `Opponent` ad hoc. They consume a view model with explicit local-player
meaning.

### Better Tests

Tests can be separated cleanly:

- raw sync/import tests validate server-to-storage fidelity
- projection tests validate local-player interpretation

This prevents incorrect perspective assumptions from becoming baked into unrelated
tests.

## Migration Plan

### Phase 1: Immediate Bug Fix

Already done:

- correct the current `I Won / I Lost` winner mapping
- update the targeted ghost filter test

This keeps behavior correct while the larger cleanup is still pending.

### Phase 2: Introduce Explicit Projection

- add a raw ghost battle repository model if needed
- add a dedicated ghost perspective adapter or view-model builder
- move all ghost outcome and participant translation into that adapter
- make Ghost Panel filtering and display use the projected model

At this phase, storage can still temporarily remain flipped if needed, but the goal is
to centralize perspective logic before changing persistence.

### Phase 3: Stop Flipping During Import

- remove participant and outcome flipping from `GhostBattleApiClient`
- persist ghost battle fields as returned by the server
- update repository tests to assert raw-field fidelity
- keep UI behavior unchanged by continuing to project to local perspective at read time

## Risks

### Mixed Migration State

If some ghost UI paths use the projection layer and others still read raw repository
fields directly, the codebase can temporarily become more confusing, not less.

Mitigation:

- migrate all ghost display and filter paths together
- treat direct ghost field reads in UI code as cleanup targets

### Existing Data Compatibility

Old local SQLite rows may already contain flipped semantics.

Mitigation options:

- accept that only newly synced rows use raw semantics and clear old ghost rows
- or add a migration/version marker if preserving historical ghost data matters

For the current feature set, resyncing ghost rows is likely the simplest option.

## Recommendation

Keep local-player perspective as a product behavior, but stop encoding that perspective
inside persisted ghost battle facts.

The preferred architecture is:

- store raw against-me ghost battle data unchanged
- project to local-player perspective in one explicit adapter
- let the History Panel consume only the projected view model

This preserves the current UX while removing the semantic trap that caused the recent
outcome-filter bug.
