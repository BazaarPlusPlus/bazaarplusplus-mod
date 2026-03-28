# ModCFServer Opponent Battle Query Design

## Goal

Add a server-side query path that lets the mod fetch recent PVP battles where the current user was
the opponent and the uploader won, then defer replay payload download until the user explicitly asks
for replay.

This supports the local `HistoryPanel` "imported ghost battles" flow:

1. query recent qualifying battles
2. import battle manifests and snapshots into local history
3. request a signed payload download link only when the user clicks replay
4. download and persist the payload locally
5. replay through the existing local `CombatReplayRuntime`

## Current State

`ModCFServer` currently stores only:

- registered client identity in `registered_clients`
- run upload metadata in `run_uploads`
- replay upload object metadata in `replay_uploads`
- anti-replay nonces in `request_nonces`

It does **not** persist the uploaded run snapshot body or project uploaded `pvp_battles` into a
queryable table. As a result, the server cannot currently answer:

- "Which recent battles were wins against me?"
- "Does this battle have a replay payload available?"

## Product Requirement

For a logged-in user:

- find the user's effective Bazaar `player_account_id`
- look back over the previous 3 days of uploaded battles
- return battles where:
  - `opponent_account_id` equals the user's effective `player_account_id`
  - the uploader won
  - `combat_kind` is `PVPCombat`
- initially return battle manifest data only
- expose replay availability
- generate a short-lived payload download link on demand

Replay remains a later explicit action. The initial list endpoint must not download or proxy replay
payload bodies.

## High-Level Architecture

### Server responsibilities

1. Accept signed run uploads as today.
2. Parse the run-upload JSON body after signature verification.
3. Project `pvp_battles` entries into a query table.
4. Resolve `uid -> player_account_id` through server-owned account binding state.
5. Query recent opponent wins from projected battles.
6. Check replay availability through `replay_uploads`.
7. Generate a signed short-lived download URL for replay payload retrieval.

### Client responsibilities

1. Call the battle query endpoint.
2. Import returned battle manifests into a synthetic local history run.
3. Mark replay as "downloadable" instead of immediately available.
4. On replay click, request a signed download URL.
5. Download the payload file and save it locally as `CombatReplays/<battle_id>.payload.json`.
6. Reuse the existing replay button path once the payload exists locally.

## Data Model

### Keep existing tables

- `registered_clients`
- `run_uploads`
- `replay_uploads`
- `request_nonces`

### Add `client_uid_bindings`

Associates an authenticated site user with a mod client registration.

```text
client_id TEXT PRIMARY KEY
uid TEXT NOT NULL
bound_at_utc TEXT NOT NULL
unbound_at_utc TEXT NULL
```

Rules:

- one active `client_id -> uid`
- one `uid` may own multiple clients
- historical unbinds should remain auditable

### Add `uid_player_accounts`

Maps a site user to observed Bazaar account ids seen in uploaded battle data.

```text
uid TEXT NOT NULL
player_account_id TEXT NOT NULL
last_seen_at_utc TEXT NOT NULL
last_client_id TEXT NOT NULL
PRIMARY KEY (uid, player_account_id)
```

Rules:

- updated only by the server after a verified run upload
- derived from uploaded `pvp_battles[*].player_account_id`
- never trusted from a standalone client claim

This supports users with multiple devices and gives the query path a stable lookup from website
identity to gameplay account identity.

### Add `pvp_battles`

Projection table built from uploaded run snapshots.

```text
battle_id TEXT PRIMARY KEY
run_id TEXT NULL
source_client_id TEXT NOT NULL
source_uid TEXT NULL
recorded_at_utc TEXT NOT NULL
day INTEGER NULL
hour INTEGER NULL
encounter_id TEXT NULL
player_name TEXT NULL
player_account_id TEXT NULL
player_rank TEXT NULL
player_rating INTEGER NULL
opponent_name TEXT NULL
opponent_account_id TEXT NULL
opponent_hero TEXT NULL
opponent_rank TEXT NULL
opponent_rating INTEGER NULL
opponent_level INTEGER NULL
combat_kind TEXT NOT NULL
result TEXT NULL
winner_combatant_id TEXT NULL
loser_combatant_id TEXT NULL
payload_json TEXT NOT NULL
created_at_utc TEXT NOT NULL
updated_at_utc TEXT NOT NULL
```

Recommended indexes:

- `idx_pvp_battles_recorded_at_utc` on `(recorded_at_utc DESC)`
- `idx_pvp_battles_opponent_recent` on `(opponent_account_id, recorded_at_utc DESC)`
- `idx_pvp_battles_player_recent` on `(player_account_id, recorded_at_utc DESC)`
- `idx_pvp_battles_source_uid_recent` on `(source_uid, recorded_at_utc DESC)`

`payload_json` should store the original battle entry shape from the run upload, including:

- identity fields
- result fields
- `player_hand`
- `player_skills`
- `opponent_hand`
- `opponent_skills`

This lets the server return a battle payload that the client can import without reconstructing the
snapshot object.

## Upload Projection Flow

### `POST /runs/upload`

Keep the existing signature verification and idempotent run-upload write.

After verification succeeds:

1. Parse the JSON body.
2. Read `run_id` and `pvp_battles`.
3. Load any active `client_uid_bindings` row for `client_id`.
4. For each `pvp_battles[]` entry:
   - require `battle_id`
   - skip entries whose `combat_kind` is not `PVPCombat`
   - upsert into server `pvp_battles`
   - set `source_client_id = verified.client.client_id`
   - set `source_uid = bound uid if present`
   - store the original battle JSON in `payload_json`
5. If a bound `uid` exists and the battle exposes `player_account_id`, upsert
   `uid_player_accounts`.

### Upsert semantics

Use `battle_id` as the stable idempotency key.

On conflict:

- update scalar query columns from the latest upload
- update `payload_json`
- update `source_client_id`
- update `source_uid`
- update `updated_at_utc`

This matches the current replay-upload behavior where the latest successful upload wins by id.

## Query Semantics

### "Wins against me"

The battle record is uploader-centric:

- `player_*` fields describe the uploader
- `opponent_*` fields describe the uploader's opponent
- `result` describes the uploader's outcome

Therefore "battles where I was the opponent and the uploader won" means:

- `opponent_account_id = my resolved player_account_id`
- uploader won

The win predicate should be:

- `LOWER(result) IN ('win', 'won')`
  OR
- `winner_combatant_id = 'Player'`

The query should also require:

- `combat_kind = 'PVPCombat'`
- `recorded_at_utc >= now - 3 days`

## API Design

### Authentication model

These endpoints are user-facing and should use the website/session auth layer, not mod upload
signatures.

The Worker must resolve `uid` from the authenticated request context. The request must not supply
an arbitrary `uid`.

### Endpoint 1: Query recent opponent wins

`GET /me/pvp-battles/wins-against-me?days=3&limit=50`

Parameters:

- `days`
  - optional
  - default `3`
  - clamp to a small max such as `14`
- `limit`
  - optional
  - default `50`
  - clamp to a small max such as `200`

Processing:

1. resolve authenticated `uid`
2. resolve one or more `player_account_id`s from `uid_player_accounts`
3. if none exist, return an empty result with `resolved_account_ids: []`
4. query `pvp_battles` where:
   - `opponent_account_id IN (...)`
   - `combat_kind = 'PVPCombat'`
   - recent enough
   - uploader won
5. left join `replay_uploads` on `battle_id`
6. return projected battle payloads ordered by `recorded_at_utc DESC, battle_id DESC`

Response shape:

```json
{
  "resolved_account_ids": ["player-account-001"],
  "from_utc": "2026-03-26T00:00:00.000Z",
  "to_utc": "2026-03-29T00:00:00.000Z",
  "battles": [
    {
      "battle_id": "battle-001",
      "run_id": "run-123",
      "recorded_at_utc": "2026-03-28T10:20:00.000Z",
      "day": 8,
      "hour": 1,
      "encounter_id": "encounter-001",
      "player_name": "Winner",
      "player_account_id": "winner-001",
      "player_rank": "Gold 2",
      "player_rating": 1420,
      "opponent_name": "You",
      "opponent_account_id": "player-account-001",
      "opponent_hero": "Vanessa",
      "opponent_rank": "Gold",
      "opponent_rating": 1337,
      "opponent_level": 9,
      "combat_kind": "PVPCombat",
      "result": "win",
      "winner_combatant_id": "Player",
      "loser_combatant_id": "Opponent",
      "player_hand": {},
      "player_skills": {},
      "opponent_hand": {},
      "opponent_skills": {},
      "replay": {
        "available": true
      }
    }
  ]
}
```

Notes:

- return the battle payload in the same structural shape already produced by local run export and
  run upload
- do not return `object_key`
- do not inline replay payload data

### Endpoint 2: Request a replay download link

`POST /me/pvp-battles/:battleId/replay-download-link`

Processing:

1. resolve authenticated `uid`
2. resolve `uid -> player_account_id`
3. load the requested battle
4. verify the battle belongs to the caller's accessible set:
   - `opponent_account_id IN caller account ids`
5. load `replay_uploads` row by `battle_id`
6. if missing, return `404` or `409 replay_unavailable`
7. generate a short-lived signed download token
8. return a Worker-hosted download URL

Response shape:

```json
{
  "battle_id": "battle-001",
  "expires_at_utc": "2026-03-29T12:05:00.000Z",
  "download_url": "https://example.com/replays/download?battle_id=battle-001&exp=1743249900&sig=..."
}
```

### Endpoint 3: Download replay payload

`GET /replays/download?battle_id=...&exp=...&sig=...`

Processing:

1. validate expiration
2. validate HMAC signature using a server secret
3. load `replay_uploads.object_key`
4. read the R2 object
5. stream it back as `application/json`

This endpoint may be anonymous if the signature is self-authenticating and short-lived. If desired,
it may also require the same user session in addition to the token.

## Download Link Signing

Add a Worker secret such as:

- `REPLAY_DOWNLOAD_SECRET`

Suggested canonical string:

```text
GET
/replays/download
{battle_id}
{exp_unix_seconds}
```

Signature:

- base64url HMAC-SHA256

Token lifetime:

- 5 minutes by default

Do **not** return raw `object_key` or a permanent bucket path to the client.

## Client Integration

### Phase 1: manifest import only

The mod calls `GET /me/pvp-battles/wins-against-me`.

For each returned battle:

1. create or reuse a synthetic imported run
2. import the battle manifest into local history storage
3. do **not** write a local replay payload file yet
4. mark replay state as downloadable if `replay.available == true`

Recommended synthetic run grouping:

- one imported run per remote ghost/build identity
- if build identity is not available yet, start with one imported run per account or one global
  imported run

### Phase 2: replay-on-demand

When the user clicks replay for an imported battle:

1. if local payload already exists, replay immediately
2. otherwise call `POST /me/pvp-battles/:battleId/replay-download-link`
3. download the payload JSON
4. save it locally as `CombatReplays/<battle_id>.payload.json`
5. reuse the existing local replay button logic

This keeps the local UX fast while avoiding a bulk payload download during initial history sync.

## Failure Handling

### Query endpoint

- if the user has no resolved `player_account_id`, return `200` with an empty list
- if the user is not authenticated, return `401`
- if the user is authenticated but not yet bound to any mod client, return `200` with an empty list

### Download-link endpoint

- `401` unauthenticated
- `403` battle is not visible to the caller
- `404` unknown battle
- `409` replay payload not uploaded yet

### Download endpoint

- `400` malformed token
- `403` invalid signature
- `410` expired token
- `404` replay object missing

## Privacy and Trust Boundaries

- upload signature proves install ownership, not website-user identity
- website auth proves `uid`, not device ownership
- server-side binding connects the two
- the client must never directly claim `uid` or `player_account_id` as a trusted query selector
- replay download links must be short-lived and server-signed

## Recommended Rollout

### Step 1

Extend `POST /runs/upload` to parse and project `pvp_battles`.

### Step 2

Add `client_uid_bindings` and `uid_player_accounts`.

### Step 3

Add `GET /me/pvp-battles/wins-against-me`.

### Step 4

Add signed replay download link generation and `GET /replays/download`.

### Step 5

Wire the mod client to:

- import manifest-only battles into `HistoryPanel`
- request replay payloads on demand

## Open Questions

1. Should one `uid` resolve to multiple `player_account_id`s at query time?
   - recommended: yes
   - reason: multiple devices or account migrations can legitimately produce more than one observed
     account id

2. Should imported battles be grouped by uploader account, by build fingerprint, or into one global
   synthetic run?
   - recommended first version: one global imported run
   - upgrade later once the server returns a stable grouping key

3. Should the replay download endpoint require both user auth and a signed token?
   - recommended first version: signed token only, short lifetime
   - reason: simpler for the mod's HTTP client

4. Should historical uploads be backfilled to `uid_player_accounts` after a new binding is created?
   - recommended: yes, asynchronously
   - not required for first implementation
