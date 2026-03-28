# Client/Server Run And Replay Sync

## Scope

This document summarizes the current BazaarPlusPlus client and `ModCFServer` interaction model for:

- local client-side persistence
- upload payloads sent from the mod to the server
- server-side storage and battle projection
- the current and planned query path for imported battle history and delayed replay download

Status labels used below:

- `Implemented`: already present in code
- `Planned`: designed, but not exposed as a public server API yet

## End-To-End Overview

There are two different data channels:

1. run sync
   - uploads a full completed-run snapshot
   - used for server-side battle projection and future queries
2. replay sync
   - uploads one replay payload per battle
   - used only when a battle needs native replay later

They intentionally serve different purposes:

- run upload carries structured business data
- replay upload carries the raw three-message replay bundle

## 1. Local Client Data Structures

### 1.1 Local SQLite database

Implemented.

The client stores run and battle history in `bazaarplusplus.db`. The core schema is defined in
`Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs`.

Main tables:

- `runs`
  - one row per run
  - stores run identity and summary fields such as `hero`, `game_mode`, `started_at_utc`, `status`
- `run_events`
  - append-only event log keyed by `(run_id, seq)`
- `run_checkpoints`
  - latest checkpoint snapshot for a run
- `run_status`
  - completed/abandoned terminal status for a run
- `pvp_battles`
  - local `PvpBattleManifest` catalog
  - stores battle identity, player/opponent identity, outcome, and four snapshot JSON blobs
- `run_sync_state`
  - upload state for run snapshots
  - tracks `dirty`, retry state, and last uploaded status
- `replay_sync_state`
  - upload state for replay payloads
  - tracks `dirty`, payload hash, object key, retry state, and last error

### 1.2 Local replay payload files

Implemented.

Replay payload files are stored under:

- `<GameRoot>/BazaarPlusPlus/CombatReplays`

Each replay is saved as:

- `CombatReplays/<battle_id>.payload.json`

The payload file is separate from SQLite metadata:

- SQLite `pvp_battles` stores the battle manifest and snapshots
- the file stores the raw replay payload needed for native replay

### 1.3 Run upload payload shape

Implemented.

The client builds `RunUploadPayload` in `Game/RunLogging/Upload/RunUploadPayload.cs`.

Logical shape:

```json
{
  "schema_version": 1,
  "install_id": "string",
  "client_id": "string | null",
  "plugin_version": "string",
  "submitted_at_utc": "2026-03-29T00:00:00.000Z",
  "run_id": "string",
  "meta": {},
  "events": [],
  "checkpoint": {},
  "status": {},
  "pvp_battles": []
}
```

Field meaning:

- `meta`
  - run header/summary exported from local SQLite `runs`
- `events`
  - ordered run timeline exported from `run_events`
- `checkpoint`
  - latest checkpoint row exported from `run_checkpoints`
- `status`
  - terminal row exported from `run_status`
- `pvp_battles`
  - uploaded battle manifest objects exported from local `pvp_battles`

`RunUploadSqliteStore.TryBuildSnapshot(...)` assembles this payload directly from local SQLite.

### 1.4 Local battle manifest shape

Implemented.

The local `pvp_battles` table represents `PvpBattleManifest` from `Game/PvpBattles/PvpBattleManifest.cs`.

Logical shape:

```json
{
  "battle_id": "string",
  "run_id": "string | null",
  "recorded_at_utc": "2026-03-29T00:00:00.000Z",
  "combat_kind": "PVPCombat",
  "day": 8,
  "hour": 1,
  "encounter_id": "string | null",
  "player_name": "string | null",
  "player_account_id": "string | null",
  "player_rank": "string | null",
  "player_rating": 1420,
  "opponent_name": "string | null",
  "opponent_hero": "string | null",
  "opponent_rank": "string | null",
  "opponent_rating": 1337,
  "opponent_level": 9,
  "opponent_account_id": "string | null",
  "result": "win | loss | null",
  "winner_combatant_id": "Player | Opponent | null",
  "loser_combatant_id": "Player | Opponent | null",
  "player_hand": {},
  "player_skills": {},
  "opponent_hand": {},
  "opponent_skills": {}
}
```

This object is what the history UI reads locally, and it is also what the server now projects into
its own query table.

### 1.5 Local replay payload shape

Implemented.

The replay file stores `PvpReplayPayload` from `Game/PvpBattles/PvpReplayPayload.cs`.

Logical shape:

```json
{
  "battle_id": "string",
  "version": 1,
  "spawn_message_base64": "base64",
  "combat_message_base64": "base64",
  "despawn_message_base64": "base64"
}
```

This payload is only for replay. It is not used as a query source.

## 2. Upload To Server Data Structures

### 2.1 Client registration

Implemented.

Route:

- `POST /clients/register`

Purpose:

- register one mod install and its public key
- get a stable `client_id`

Request shape:

```json
{
  "install_id": "string",
  "plugin_version": "string",
  "purpose": "runs | replays",
  "public_key": {
    "modulus_b64": "base64",
    "exponent_b64": "base64"
  }
}
```

Current success response:

```json
{
  "client_id": "string",
  "purpose": "runs | replays",
  "status": "registered"
}
```

Server persistence:

- `registered_clients`
  - `client_id`
  - `install_id`
  - `purpose`
  - `modulus_b64`
  - `exponent_b64`
  - `plugin_version`
  - `registered_at_utc`

### 2.2 Signed upload contract

Implemented.

Both `/runs/upload` and `/replays/upload` use the same signed-request verification path.

Required headers:

- `X-BPP-Client-Id`
- `X-BPP-Install-Id`
- `X-BPP-Timestamp`
- `X-BPP-Nonce`
- `X-BPP-Content-SHA256`
- `X-BPP-Signature-Alg`
- `X-BPP-Signature`

Route-specific optional headers:

- `X-BPP-Run-Id`
- `X-BPP-Battle-Id`

Server verification rules:

- load the registered client by `client_id`
- require matching `install_id`
- require matching registered `purpose`
- reject timestamps outside the skew window
- reject repeated nonces
- recompute `SHA-256` over the raw request body
- rebuild the canonical request string
- verify the RSA PKCS#1 SHA-256 signature

Nonce persistence:

- `request_nonces`

### 2.3 Run upload

Implemented.

Route:

- `POST /runs/upload`

Request body:

- the full `RunUploadPayload`

Additional route-specific header:

- `X-BPP-Run-Id`

Current validation rules:

- body must be valid JSON
- request `purpose` must resolve to `runs`
- `X-BPP-Run-Id` and body `run_id` must not disagree
- at least one run id source must be present

Current server persistence is now two-layered.

#### Raw accepted upload: `run_uploads`

Implemented.

Each accepted run upload is stored in `run_uploads`:

```sql
run_id TEXT PRIMARY KEY,
client_id TEXT NOT NULL,
install_id TEXT NOT NULL,
payload_sha256 TEXT NOT NULL,
payload_json TEXT NOT NULL,
uploaded_at_utc TEXT NOT NULL,
projection_status TEXT NOT NULL,
projected_at_utc TEXT NULL,
projection_error TEXT NULL
```

Meaning:

- `payload_json`
  - the raw accepted run upload body
- `projection_status`
  - `pending`, `projected`, or `failed`
- `projected_at_utc`
  - when battle projection finished successfully
- `projection_error`
  - last projection failure message if projection failed

#### Projected battle query data: `pvp_battles`

Implemented.

After the run upload is accepted, the server parses `pvp_battles` from the uploaded run body and
projects them into D1 `pvp_battles`.

Current rules:

- only battles with `combat_kind == "PVPCombat"` are projected
- `battle_id` is required
- re-uploading the same run from the same client first deletes that run's previous projected battles
  for that client, then inserts the current set
- `payload_json` stores the original uploaded battle object

Projected columns:

```sql
battle_id TEXT PRIMARY KEY,
run_id TEXT NULL,
source_client_id TEXT NOT NULL,
recorded_at_utc TEXT NOT NULL,
day INTEGER NULL,
hour INTEGER NULL,
encounter_id TEXT NULL,
player_name TEXT NULL,
player_account_id TEXT NULL,
player_rank TEXT NULL,
player_rating INTEGER NULL,
opponent_name TEXT NULL,
opponent_account_id TEXT NULL,
opponent_hero TEXT NULL,
opponent_rank TEXT NULL,
opponent_rating INTEGER NULL,
opponent_level INTEGER NULL,
combat_kind TEXT NOT NULL,
result TEXT NULL,
winner_combatant_id TEXT NULL,
loser_combatant_id TEXT NULL,
payload_json TEXT NOT NULL,
created_at_utc TEXT NOT NULL,
updated_at_utc TEXT NOT NULL
```

#### UID/account projection support

Implemented at the schema and projection layer.

The server also maintains two identity-support tables:

- `client_uid_bindings`
  - active or historical mapping from website `uid` to `client_id`
- `uid_player_accounts`
  - observed mapping from website `uid` to Bazaar `player_account_id`

Current write behavior:

- if the uploading client has an active bound `uid`
- and a projected battle exposes `player_account_id`
- the server upserts that pair into `uid_player_accounts`

This is what prepares the future query path for "wins against me".

### 2.4 Replay upload

Implemented.

Route:

- `POST /replays/upload`

Request body:

- one `PvpReplayPayload` JSON document

Additional route-specific headers:

- `X-BPP-Battle-Id`
- `X-BPP-Run-Id` optional

Server storage model:

- the raw replay payload body is stored in R2
- D1 only stores upload metadata

Current R2 object key shape:

- `combat-replays/replays/{client_id}/{battle_id}/{payload_hash_prefix}.payload.json`

Current D1 table:

```sql
battle_id TEXT PRIMARY KEY,
client_id TEXT NOT NULL,
install_id TEXT NOT NULL,
run_id TEXT NULL,
payload_sha256 TEXT NOT NULL,
object_key TEXT NOT NULL,
uploaded_at_utc TEXT NOT NULL
```

Meaning:

- `run upload` is the source for battle querying
- `replay upload` is the source for replay retrieval

## 3. Concrete Query Model

### 3.1 Current public server API status

Current public routes are:

- `POST /health`
- `POST /clients/register`
- `POST /runs/upload`
- `POST /replays/upload`

There is currently no public battle query endpoint and no public replay download endpoint.

What already exists today is the storage needed for those queries:

- raw run uploads in `run_uploads`
- projected battle rows in `pvp_battles`
- replay availability rows in `replay_uploads`
- `uid -> player_account_id` support in `uid_player_accounts`

### 3.2 Query semantics for "wins against me"

Planned.

The server-side battle rows are uploader-centric:

- `player_*` describes the uploader
- `opponent_*` describes the uploader's opponent
- `result` is the uploader's result

So the condition "recent battles where I was the opponent and the uploader won" means:

- `opponent_account_id IN (my resolved player_account_id set)`
- `combat_kind = 'PVPCombat'`
- recent enough, currently designed as the last 3 days
- the uploader won

Recommended win predicate:

```sql
LOWER(COALESCE(result, '')) IN ('win', 'won')
OR winner_combatant_id = 'Player'
```

### 3.3 Query preparation SQL

Planned.

The following SQL shapes describe the intended read path against the data that is already stored in
D1.

Resolve the caller's known Bazaar account ids:

```sql
SELECT player_account_id
FROM uid_player_accounts
WHERE uid = ?
ORDER BY last_seen_at_utc DESC;
```

Query recent qualifying battles plus replay availability:

```sql
SELECT
  pb.battle_id,
  pb.payload_json,
  CASE WHEN ru.battle_id IS NULL THEN 0 ELSE 1 END AS replay_available
FROM pvp_battles AS pb
LEFT JOIN replay_uploads AS ru
  ON ru.battle_id = pb.battle_id
WHERE pb.opponent_account_id IN (
  SELECT upa.player_account_id
  FROM uid_player_accounts AS upa
  WHERE upa.uid = ?
)
  AND pb.combat_kind = 'PVPCombat'
  AND pb.recorded_at_utc >= ?
  AND (
    LOWER(COALESCE(pb.result, '')) IN ('win', 'won')
    OR pb.winner_combatant_id = 'Player'
  )
ORDER BY pb.recorded_at_utc DESC, pb.battle_id DESC
LIMIT ?;
```

Result interpretation:

- `payload_json`
  - the battle manifest object the client can import into local history
- `replay_available`
  - whether a replay payload upload exists for the same `battle_id`

### 3.4 Planned HTTP query contract

Planned.

Recommended battle list endpoint:

- `GET /me/pvp-battles/wins-against-me?days=3&limit=50`

Recommended response shape:

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

Recommended replay-link endpoint:

- `POST /me/pvp-battles/:battleId/replay-download-link`

Recommended response shape:

```json
{
  "battle_id": "battle-001",
  "expires_at_utc": "2026-03-29T12:05:00.000Z",
  "download_url": "https://example.com/replays/download?battle_id=battle-001&exp=1743249900&sig=..."
}
```

Recommended download endpoint:

- `GET /replays/download?battle_id=...&exp=...&sig=...`

### 3.5 Intended client query flow

Planned.

The intended imported-history flow is:

1. call `GET /me/pvp-battles/wins-against-me`
2. import each returned battle manifest into a synthetic local history run
3. do not download replay payloads yet
4. mark replay as downloadable if `replay.available == true`
5. when the user clicks replay:
   - request a signed replay download link
   - download the replay payload JSON
   - write `CombatReplays/<battle_id>.payload.json`
   - reuse the existing local replay path

That keeps the initial history sync light and makes replay an explicit later action.

## Summary

Today, the mod already uploads:

- full run snapshots for queryable battle projection
- replay payload files for later native replay

Today, the server already stores:

- raw accepted run uploads
- projected `pvp_battles`
- replay object metadata
- `uid -> player_account_id` support rows

What is still missing is only the user-facing query API:

- battle list endpoint
- replay download-link endpoint
- signed replay download endpoint
