# Mod API Unification Design

## Goal

Replace the split run/replay upload model with a clean-break Mod API based on:

- one client identity per install
- one player binding per client
- run summaries as lightweight audit/history uploads
- battle artifacts as the only remote source of truth for ghost queries and replay downloads

This design intentionally drops legacy compatibility. No old protocol, table, or client-state migration is preserved.

## Scope

This design covers the full remote flow:

- client registration
- player binding
- run summary upload
- battle artifact upload
- ghost battle query
- replay link creation
- replay download

It also covers the corresponding local mod responsibilities and the new D1/R2 model for `ModCFServer`.

## Non-Goals

- compatibility with `purpose = runs` / `purpose = replays`
- compatibility with old endpoints like `/runs/upload` or `/battles/upload`
- migration of existing D1 data
- restoration of old local scoped client state
- preserving run-side battle projection lifecycle state

## Design Summary

The system is battle-first.

`runs` exist for summary, audit, and possible future run history features. They do not power ghost queries.

`battles` are the only queryable source for:

- remote ghost battle discovery
- replay availability
- replay payload lookup

The client registers once, binds once, and uses the same signed request stack for every route.

## API Surface

All routes are rooted at a single API base URL. The client must not derive routes by rewriting other endpoints.

### Routes

- `POST /clients/register`
- `POST /clients/bind-player`
- `POST /runs`
- `POST /battles`
- `GET /players/me/ghost-battles`
- `POST /players/me/ghost-battles/:battleId/replay-link`
- `GET /replays/:token`

### Signing Model

All authenticated routes use the same signed request contract:

- `x-bpp-client-id`
- `x-bpp-install-id`
- `x-bpp-timestamp`
- `x-bpp-content-sha256`
- `x-bpp-signature-alg`
- `x-bpp-signature`

There is no `purpose` field in registration or verification.

## Worker Data Model

The new schema contains exactly five tables.

### `clients`

Stores the registered public key and install metadata.

Columns:

- `client_id`
- `install_id`
- `modulus_b64`
- `exponent_b64`
- `plugin_version`
- `registered_at_utc`
- `last_seen_at_utc`
- `revoked_at_utc`

### `player_links`

Stores the current player account represented by a client.

Columns:

- `client_id`
- `player_account_id`
- `bound_at_utc`
- `last_confirmed_at_utc`

This replaces both active binding and observation tables from the legacy model.

### `runs`

Stores run-level summary data only.

Columns:

- `run_id`
- `client_id`
- `player_account_id`
- `status`
- `hero_id`
- `hero_name`
- `started_at_utc`
- `ended_at_utc`
- `final_day`
- `final_wins`
- `final_losses`
- `mmr`
- `summary_schema_version`
- `summary_object_key`
- `created_at_utc`
- `updated_at_utc`

### `battles`

Stores queryable battle metadata and replay storage metadata.

Columns:

- `battle_id`
- `run_id`
- `client_id`
- `uploader_player_account_id`
- `recorded_at_utc`
- `day`
- `hour`
- `player_name`
- `player_account_id`
- `player_hero`
- `player_rank`
- `player_rating`
- `player_level`
- `opponent_name`
- `opponent_account_id`
- `opponent_hero`
- `opponent_rank`
- `opponent_rating`
- `opponent_level`
- `combat_kind`
- `result`
- `winner_combatant_id`
- `loser_combatant_id`
- `replay_schema_version`
- `replay_object_key`
- `replay_size_bytes`
- `created_at_utc`
- `updated_at_utc`

`encounter_id` is intentionally omitted.

### `replay_tokens`

Stores replay download authorization tokens.

Columns:

- `token`
- `battle_id`
- `requested_by_player_account_id`
- `expires_at_utc`
- `created_at_utc`
- `used_at_utc`
- `revoked_at_utc`

## R2 Storage Model

R2 stores payload-sized artifacts, not queryable business state.

### Run summaries

Stored under a run-summary prefix, for example:

- `run-summaries/<client_id>/<run_id>/<payload_hash>.json`

### Battle replay artifacts

Stored under a battle replay prefix, for example:

- `battle-replays/<client_id>/<battle_id>/<payload_hash>.json`

The Worker writes queryable battle fields into D1 and the replay payload bytes into R2.

## Client Responsibilities

The mod should be reorganized around one shared Mod API session layer.

### Shared client state

Persist exactly one client/session record:

- `client_id`
- bound `player_account_id`
- install identity
- keypair metadata

Do not keep scoped client ids for run/replay purposes.

### Shared infrastructure

The client should expose:

- a single API base URL
- a single route builder
- a single request signer
- a single registration client
- a single client-state store

### Run summary upload

Run upload becomes summary-only.

It must not include:

- `pvp_battles`
- replay payloads
- event-heavy historical bodies that only existed for old projection logic

### Battle artifact upload

Battle upload becomes the authoritative remote artifact for replay and ghost features.

It includes:

- top-level `battle_id`
- optional `run_id`
- `battle_manifest`
- `replay_payload`

## Flow Definitions

### Register client

1. Client loads or creates install identity and keypair.
2. Client calls `POST /clients/register`.
3. Worker returns a stable `client_id`.
4. Client persists that `client_id`.

### Bind player

1. Client detects current `player_account_id`.
2. Client sends a signed request to `POST /clients/bind-player`.
3. Worker upserts `player_links`.
4. Future ghost/replay requests resolve identity through that link.

### Upload run summary

1. Client builds a compact run summary after run completion.
2. Client sends it to `POST /runs`.
3. Worker verifies the client, stores the payload in R2, and upserts the `runs` row.

### Upload battle artifact

1. Client builds a battle artifact from manifest plus replay payload.
2. Client sends it to `POST /battles`.
3. Worker verifies the payload, stores replay bytes in R2, and upserts the `battles` row.

### Query ghost battles

1. Client sends a signed request to `GET /players/me/ghost-battles`.
2. Worker resolves the bound player via `player_links`.
3. Worker queries `battles` where `opponent_account_id` matches the bound player.

### Create replay link

1. Client requests `POST /players/me/ghost-battles/:battleId/replay-link`.
2. Worker verifies that the requested battle is visible to the bound player.
3. Worker creates a short-lived `replay_tokens` row.
4. Worker returns `GET /replays/:token`.

### Download replay

1. Client requests `GET /replays/:token`.
2. Worker verifies token validity.
3. Worker resolves the `battles.replay_object_key`.
4. Worker returns the replay payload from R2.

## Removed Legacy Concepts

The new model removes these concepts entirely:

- registration purpose
- separate run/replay client identities
- `RunUploadScopes`
- derived battle upload endpoint from run upload endpoint
- derived bind/query endpoints from unrelated upload endpoints
- `run_uploads` projection status lifecycle
- `registered_clients`
- `client_player_account_bindings`
- `client_player_account_observations`
- `run_uploads`
- `pvp_battles`

## Configuration Model

The mod should keep one base URL constant or config entry, for example:

- `ApiBaseUrl = https://mod-api.bazaarplusplus.com`

Every route must be built from that base.

The client must not store:

- upload endpoint strings per feature
- registration endpoint strings per feature
- any route-scope-derived client ids

## Verification Strategy

Verification should remain targeted.

### Worker

Primary test seams:

- schema tests
- register client tests
- bind player tests
- run summary upload tests
- battle artifact upload tests
- ghost query tests
- replay token/download tests

### Mod client

Primary test seams:

- run upload auth tests
- run upload sync tests
- combat replay upload sync tests
- ghost battle sync tests

Do not default to full-repo build validation while the work is still isolated to this feature area.

## Clean-Break Cutover

This design assumes:

- no D1 data migration
- no legacy endpoint compatibility
- no client-state compatibility layer

After rollout:

- the client re-registers
- the client re-binds the current player
- only the new routes and schema are valid

## Implementation Companion

The execution checklist for this design lives in:

- [2026-04-01-mod-api-unification-plan.md](/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/docs/plans/2026-04-01-mod-api-unification-plan.md)
