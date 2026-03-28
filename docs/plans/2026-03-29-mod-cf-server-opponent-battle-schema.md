# ModCFServer Opponent Battle Schema

## Scope

This document defines the server-side schema needed to support:

- projecting uploaded `pvp_battles` from `POST /runs/upload`
- resolving a logged-in site user to one or more observed Bazaar `player_account_id`s
- querying recent battles where the caller was the opponent and the uploader won
- checking whether a replay payload exists for a given `battle_id`

This document intentionally does **not** define HTTP routes or handler logic. Interface work is
deferred.

## Current Tables

`ModCFServer` already creates:

### `registered_clients`

```sql
CREATE TABLE IF NOT EXISTS registered_clients (
  client_id TEXT PRIMARY KEY,
  install_id TEXT NOT NULL,
  purpose TEXT NOT NULL,
  modulus_b64 TEXT NOT NULL,
  exponent_b64 TEXT NOT NULL,
  plugin_version TEXT NULL,
  registered_at_utc TEXT NOT NULL
);
```

### `replay_uploads`

```sql
CREATE TABLE IF NOT EXISTS replay_uploads (
  battle_id TEXT PRIMARY KEY,
  client_id TEXT NOT NULL,
  install_id TEXT NOT NULL,
  run_id TEXT NULL,
  payload_sha256 TEXT NOT NULL,
  object_key TEXT NOT NULL,
  uploaded_at_utc TEXT NOT NULL
);
```

### `run_uploads`

```sql
CREATE TABLE IF NOT EXISTS run_uploads (
  run_id TEXT PRIMARY KEY,
  client_id TEXT NOT NULL,
  install_id TEXT NOT NULL,
  payload_sha256 TEXT NOT NULL,
  uploaded_at_utc TEXT NOT NULL
);
```

### `request_nonces`

```sql
CREATE TABLE IF NOT EXISTS request_nonces (
  nonce_key TEXT PRIMARY KEY,
  created_at_utc TEXT NOT NULL
);
```

## New Tables

### `client_uid_bindings`

Associates a site user account with a registered mod client.

```sql
CREATE TABLE IF NOT EXISTS client_uid_bindings (
  binding_id TEXT PRIMARY KEY,
  client_id TEXT NOT NULL,
  uid TEXT NOT NULL,
  bound_at_utc TEXT NOT NULL,
  unbound_at_utc TEXT NULL
);
```

Indexes:

```sql
CREATE INDEX IF NOT EXISTS idx_client_uid_bindings_uid
  ON client_uid_bindings(uid);

CREATE INDEX IF NOT EXISTS idx_client_uid_bindings_uid_active
  ON client_uid_bindings(uid, unbound_at_utc);

CREATE INDEX IF NOT EXISTS idx_client_uid_bindings_client
  ON client_uid_bindings(client_id);

CREATE UNIQUE INDEX IF NOT EXISTS idx_client_uid_bindings_client_active
  ON client_uid_bindings(client_id)
  WHERE unbound_at_utc IS NULL;
```

Rules:

- one active row per `client_id`
- one `uid` may own many clients
- `unbound_at_utc IS NULL` means active binding

Notes:

- `binding_id` exists so one client can accumulate binding history over time
- no foreign key to `registered_clients`
- D1 can support the FK, but leaving this as a logical association keeps migration simpler and avoids
  binding lifecycle coupling to client registration cleanup

### `uid_player_accounts`

Maps a website user to one or more observed Bazaar account ids seen in uploaded battle manifests.

```sql
CREATE TABLE IF NOT EXISTS uid_player_accounts (
  uid TEXT NOT NULL,
  player_account_id TEXT NOT NULL,
  first_seen_at_utc TEXT NOT NULL,
  last_seen_at_utc TEXT NOT NULL,
  last_client_id TEXT NOT NULL,
  PRIMARY KEY (uid, player_account_id)
);
```

Indexes:

```sql
CREATE INDEX IF NOT EXISTS idx_uid_player_accounts_uid
  ON uid_player_accounts(uid);

CREATE INDEX IF NOT EXISTS idx_uid_player_accounts_account
  ON uid_player_accounts(player_account_id);
```

Rules:

- populated only from verified run uploads
- never trusted from an arbitrary client claim
- updated when an active bound client uploads a battle containing `player_account_id`

Recommended upsert semantics:

- preserve `first_seen_at_utc`
- refresh `last_seen_at_utc`
- refresh `last_client_id`

### `pvp_battles`

Server-side query projection for uploaded battle manifests.

```sql
CREATE TABLE IF NOT EXISTS pvp_battles (
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
);
```

Indexes:

```sql
CREATE INDEX IF NOT EXISTS idx_pvp_battles_recorded_at_utc
  ON pvp_battles(recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_opponent_recent
  ON pvp_battles(opponent_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_player_recent
  ON pvp_battles(player_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_combat_kind_recent
  ON pvp_battles(combat_kind, recorded_at_utc DESC);
```

Optional composite index if recent-opponent-win queries dominate:

```sql
CREATE INDEX IF NOT EXISTS idx_pvp_battles_opponent_kind_result_recent
  ON pvp_battles(opponent_account_id, combat_kind, result, recorded_at_utc DESC);
```

Rules:

- one row per `battle_id`
- latest upload wins on conflict
- `payload_json` stores the original uploaded battle object
- query filters should use scalar columns, not JSON extraction
- uploader website identity should be derived through `source_client_id -> active/historical binding`, not
  duplicated into this table

## Why `payload_json` Still Matters

The scalar columns support filtering and ordering. `payload_json` exists so the server can later
return a battle payload in nearly the same shape the mod already understands locally, without
reconstructing:

- `player_hand`
- `player_skills`
- `opponent_hand`
- `opponent_skills`

The projection should therefore preserve the full battle object, not a reduced summary.

## Data Ownership and Trust

### Trusted enough to store

After run-upload signature verification, the server may trust:

- which `client_id` uploaded the body
- that the body was signed by the registered install
- that `pvp_battles` came from that verified upload

### Not trusted as identity proof

The server must **not** treat these fields as authoritative proof of website identity:

- `player_account_id`
- `opponent_account_id`
- `uid` supplied by a client body or query

Instead:

- `uid` comes from website auth / binding state
- `player_account_id` becomes queryable only after the server observes it in verified uploads and
  associates it to a bound `uid`

## Recommended Migration Shape

If `ensureSchema()` remains monolithic for now, extend it in this order:

1. `client_uid_bindings`
2. `uid_player_accounts`
3. `pvp_battles`
4. indexes

Suggested SQL block:

```sql
CREATE TABLE IF NOT EXISTS client_uid_bindings (
  binding_id TEXT PRIMARY KEY,
  client_id TEXT NOT NULL,
  uid TEXT NOT NULL,
  bound_at_utc TEXT NOT NULL,
  unbound_at_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS uid_player_accounts (
  uid TEXT NOT NULL,
  player_account_id TEXT NOT NULL,
  first_seen_at_utc TEXT NOT NULL,
  last_seen_at_utc TEXT NOT NULL,
  last_client_id TEXT NOT NULL,
  PRIMARY KEY (uid, player_account_id)
);

CREATE TABLE IF NOT EXISTS pvp_battles (
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
);

CREATE INDEX IF NOT EXISTS idx_client_uid_bindings_uid
  ON client_uid_bindings(uid);

CREATE INDEX IF NOT EXISTS idx_client_uid_bindings_uid_active
  ON client_uid_bindings(uid, unbound_at_utc);

CREATE INDEX IF NOT EXISTS idx_client_uid_bindings_client
  ON client_uid_bindings(client_id);

CREATE UNIQUE INDEX IF NOT EXISTS idx_client_uid_bindings_client_active
  ON client_uid_bindings(client_id)
  WHERE unbound_at_utc IS NULL;

CREATE INDEX IF NOT EXISTS idx_uid_player_accounts_uid
  ON uid_player_accounts(uid);

CREATE INDEX IF NOT EXISTS idx_uid_player_accounts_account
  ON uid_player_accounts(player_account_id);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_recorded_at_utc
  ON pvp_battles(recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_opponent_recent
  ON pvp_battles(opponent_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_player_recent
  ON pvp_battles(player_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_combat_kind_recent
  ON pvp_battles(combat_kind, recorded_at_utc DESC);
```

## Write Patterns

### `client_uid_bindings`

Writes come from website-side binding flows, not mod upload flows.

Expected operations:

- create active binding
- mark binding inactive by setting `unbound_at_utc`
- query active bindings by `client_id`
- list active clients by `uid`

### `uid_player_accounts`

Writes come from run-upload projection.

Expected operation:

```sql
INSERT INTO uid_player_accounts (
  uid,
  player_account_id,
  first_seen_at_utc,
  last_seen_at_utc,
  last_client_id
) VALUES (?, ?, ?, ?, ?)
ON CONFLICT(uid, player_account_id) DO UPDATE SET
  last_seen_at_utc = excluded.last_seen_at_utc,
  last_client_id = excluded.last_client_id;
```

### `pvp_battles`

Writes come from run-upload projection.

Expected operation shape:

```sql
INSERT INTO pvp_battles (
  battle_id,
  run_id,
  source_client_id,
  recorded_at_utc,
  day,
  hour,
  encounter_id,
  player_name,
  player_account_id,
  player_rank,
  player_rating,
  opponent_name,
  opponent_account_id,
  opponent_hero,
  opponent_rank,
  opponent_rating,
  opponent_level,
  combat_kind,
  result,
  winner_combatant_id,
  loser_combatant_id,
  payload_json,
  created_at_utc,
  updated_at_utc
) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
ON CONFLICT(battle_id) DO UPDATE SET
  run_id = excluded.run_id,
  source_client_id = excluded.source_client_id,
  recorded_at_utc = excluded.recorded_at_utc,
  day = excluded.day,
  hour = excluded.hour,
  encounter_id = excluded.encounter_id,
  player_name = excluded.player_name,
  player_account_id = excluded.player_account_id,
  player_rank = excluded.player_rank,
  player_rating = excluded.player_rating,
  opponent_name = excluded.opponent_name,
  opponent_account_id = excluded.opponent_account_id,
  opponent_hero = excluded.opponent_hero,
  opponent_rank = excluded.opponent_rank,
  opponent_rating = excluded.opponent_rating,
  opponent_level = excluded.opponent_level,
  combat_kind = excluded.combat_kind,
  result = excluded.result,
  winner_combatant_id = excluded.winner_combatant_id,
  loser_combatant_id = excluded.loser_combatant_id,
  payload_json = excluded.payload_json,
  updated_at_utc = excluded.updated_at_utc;
```

Behavior:

- preserve original `created_at_utc`
- refresh `updated_at_utc`
- overwrite the rest with the most recent verified upload

## Read Patterns

### Resolve caller account ids

```sql
SELECT player_account_id
FROM uid_player_accounts
WHERE uid = ?
ORDER BY last_seen_at_utc DESC;
```

### Resolve recent battles where caller was the opponent and uploader won

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

Notes:

- the select can later be adjusted to return scalar columns directly instead of `payload_json`
- `replay_uploads` should be joined only to derive replay availability; `object_key` should remain an
  internal server detail

## Retention

No retention policy is required for first implementation, but schema choices should not block one.

Future cleanup candidates:

- `request_nonces`: expire old rows aggressively
- `uid_player_accounts`: keep indefinitely unless account deletion rules require cleanup
- `pvp_battles`: optional TTL or pruning by age

If battle pruning is added later, it should delete `pvp_battles` rows only. `replay_uploads` and R2
objects should be governed by their own retention policy.

## Non-Goals

This schema does not yet include:

- battle tags for imported grouping
- ghost/build fingerprints
- user-facing notes or labels
- replay download ticket tables

Those can be layered later without changing the core battle projection model.

## Recommended First Implementation Boundary

Only schema and projection support should be added in the first pass:

1. extend `ensureSchema()` with the three new tables and indexes
2. project uploaded `pvp_battles` into the new table during verified run upload
3. update `uid_player_accounts` from bound uploads

Do **not** implement:

- query endpoints
- replay download-link endpoints
- imported-history client sync

That keeps the first change focused on durable data shape and backfill readiness.
