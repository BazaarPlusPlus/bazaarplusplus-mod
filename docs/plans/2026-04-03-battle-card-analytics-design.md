# Battle Card Analytics Design

## Goal

Extend the local R2 clone metadata pipeline and HTML battle report so local analysis can answer:

- which cards appear most often in captured battle snapshots
- how player-side and opponent-side card win rates differ
- how a card's win rate changes by `battle_manifest.day`

The first version only needs card identity fields the user explicitly asked for:

- card name
- enchant
- tier

## Background

`tools/local_r2_clone/rebuild_local_r2_metadata.py` currently rebuilds a local SQLite projection from mirrored R2 JSON objects.

For battle replay payloads, the projection only stores battle-level metadata in `battle_replays`:

- battle identity
- run and client linkage
- player and opponent hero/rank metadata
- recorded timestamp
- result
- replay schema version and size

The source payload already contains `battle_manifest.snapshots`, but the local projection ignores the snapshot card contents. As a result:

- the generated HTML report can only show battle-level and hero/rank-level views
- card-level analysis requires re-reading raw JSON files
- there is no stable SQL surface for card win-rate queries

## Scope

This design covers:

- SQLite schema additions for battle-card projection
- snapshot parsing rules for metadata rebuild and sync
- normalization rules for card identity
- SQL query shapes for new card analytics
- HTML report additions for day-based card win rates
- focused verification for the Python toolchain

## Non-Goals

This design does not cover:

- changing the game mod replay payload schema
- changing remote Worker storage layout
- introducing interactive filtering in the generated HTML
- storing every raw card attribute from snapshots
- changing existing battle-level charts unless needed for layout consistency

## Current Source Shape

Each mirrored battle replay JSON contains:

- top-level battle metadata
- `battle_manifest.participants`
- `battle_manifest.outcome`
- `battle_manifest.snapshots`
- `replay_payload`

The snapshot object is the source of truth for card-level analytics. For this feature, only four snapshot groups matter:

- player hand
- player skills
- opponent hand
- opponent skills

The local analytics feature should treat player-side and opponent-side cards as separate populations instead of mixing them together.

## Design Summary

Use a metadata-first design:

1. keep `battle_replays` as the battle-level fact table
2. add a new `battle_replay_cards` detail table populated from `battle_manifest.snapshots`
3. normalize each snapshot card to a compact card identity of `card_name + enchant + tier`
4. render new report sections from SQL over that detail table

This is preferred over parsing JSON at report-render time because it keeps the report simple, makes queries reusable, and gives future analysis scripts a stable relational surface.

## Detailed Design

### 1. Add A Battle Card Detail Table

Add a new SQLite table:

```sql
CREATE TABLE IF NOT EXISTS battle_replay_cards (
  object_key TEXT NOT NULL,
  battle_id TEXT NOT NULL,
  battle_day INTEGER NULL,
  recorded_at_utc TEXT NOT NULL,
  side TEXT NOT NULL,
  card_group TEXT NOT NULL,
  result TEXT NULL,
  card_name TEXT NOT NULL,
  enchant TEXT NOT NULL,
  tier TEXT NOT NULL,
  slot_index INTEGER NOT NULL,
  battle_card_key TEXT NOT NULL,
  FOREIGN KEY (object_key) REFERENCES r2_objects(object_key) ON DELETE CASCADE
);
```

Recommended indexes:

- `idx_battle_replay_cards_side_day` on `(side, battle_day)`
- `idx_battle_replay_cards_card_lookup` on `(side, card_name, enchant, tier)`
- `idx_battle_replay_cards_battle` on `(battle_id)`

Column intent:

- `side`
  - `player` or `opponent`
- `card_group`
  - `hand` or `skills`
- `slot_index`
  - preserves slot-occurrence counting for duplicate cards in one battle
- `battle_card_key`
  - stable per-battle dedupe key built from `battle_id + side + card_name + enchant + tier`

The table is intentionally denormalized with `battle_day`, `recorded_at_utc`, and `result` copied from the parent battle so report queries do not need to join back to `battle_replays` for every aggregation.

### 2. Snapshot Parsing And Projection Rules

When projecting a `battle_replay` object:

1. continue upserting `battle_replays`
2. delete existing `battle_replay_cards` rows for that `object_key`
3. extract snapshot cards from the four supported groups
4. insert one `battle_replay_cards` row per snapshot card occurrence

Projection should be idempotent for rebuild and incremental sync:

- reprocessing the same file must replace that file's card rows cleanly
- if a battle replay object fails to parse, card rows for that `object_key` must also be removed

The detail table should be rebuilt from source payloads only. No derived rollup table is needed in the first version.

### 3. Card Identity Normalization

Only the fields requested by the user are part of card identity:

- `card_name`
- `enchant`
- `tier`

Normalization rules:

- `card_name`
  - use snapshot `Name`
  - if blank or missing, skip the row because the card cannot be grouped meaningfully
- `enchant`
  - use snapshot `Enchant`
  - if blank or missing, normalize to `None`
- `tier`
  - use snapshot `Tier`
  - if blank or missing, normalize to `Unknown`

The first version should not include:

- template id
- size
- socket position in the identity key
- attributes
- tags

That keeps the statistics aligned with the user's request and avoids fragmenting the same card into too many buckets.

### 4. Counting Semantics

The report needs two counting modes, but only one should drive the default win-rate ranking.

#### Default mode: battle-deduped presence

For win-rate calculations and "top cards" ranking, count a card once per battle per side using `battle_card_key`.

This means:

- if the same player-side card appears multiple times in one battle, it still contributes one battle-presence vote
- duplicate copies do not artificially amplify win rate or popularity

This is the recommended default because it answers: "when this side had this card identity in the fight, how often did that side win?"

#### Secondary mode: slot occurrences

Also expose raw occurrence counts using all rows in `battle_replay_cards`.

This answers:

- how often duplicate copies appear
- whether certain cards commonly show up in multiple slots

The HTML report should show both:

- deduped battle count
- raw occurrence count

but all headline win-rate tables and charts should sort by deduped battle count first.

### 5. Win-Rate Semantics By Side

`result` in `battle_manifest.outcome` is stored from the uploader/player perspective.

Therefore:

- for `side = player`, a win is `result = 'win'`
- for `side = opponent`, a win is `result = 'loss'`

This inversion must be explicit in report SQL. Opponent-side card win rate is not the same as player-side win rate over the same rows.

Draws, missing results, and unsupported result values should be excluded from win-rate denominators while still being eligible for raw appearance counts if needed.

### 6. Report Additions

Add three new analytical blocks to `tools/local_r2_clone/render_battles_charts.py`.

#### 6.1 Player Card Win Rates By Day

Show the most-seen player-side cards grouped by `battle_day`.

Recommended first version:

- table-first presentation for clarity
- columns:
  - `Day`
  - `Card`
  - `Enchant`
  - `Tier`
  - `Battles`
  - `Occurrences`
  - `Wins`
  - `Win Rate %`

Sort order:

- `battle_day` ascending
- `Battles` descending within day
- `Win Rate %` descending
- card identity ascending

Cap the table to a reasonable number of rows per day, such as top 8.

#### 6.2 Opponent Card Win Rates By Day

Mirror the same view for `side = opponent`, using the inverted win logic described above.

This makes it easy to compare:

- which cards are common threats on a given day
- whether some cards are strong only when used by opponents

#### 6.3 Top Cards Overview

Add two summary tables:

- `Top Player Cards`
- `Top Opponent Cards`

Columns:

- `Card`
- `Enchant`
- `Tier`
- `Days Seen`
- `Battles`
- `Occurrences`
- `Wins`
- `Win Rate %`

These tables should aggregate across all days and give a compact leaderboard before the day breakdown tables.

### 7. Query Shape

The report only needs SQL over `battle_replay_cards`.

Core aggregate dimensions:

- `side`
- `battle_day`
- `card_name`
- `enchant`
- `tier`

Core measures:

- `COUNT(DISTINCT battle_card_key)` as deduped battles
- `COUNT(*)` as occurrences
- side-aware wins
- side-aware win rate percentage
- `COUNT(DISTINCT battle_day)` as days seen for global tables

The implementation should keep each query in a dedicated helper similar to the existing hero/rank query helpers.

### 8. Error Handling And Compatibility

If a battle payload contains malformed or partial snapshot data:

- do not fail the whole sync run unless the battle payload itself is invalid
- skip only unusable snapshot card rows where card identity cannot be normalized

If a payload has no supported snapshot card groups:

- project the battle row normally
- insert zero card rows

This keeps old or partial payloads analyzable at the battle level even when card analytics are unavailable.

## Expected Outcome

After this change, a local metadata rebuild should produce:

- existing battle-level analytics
- player-side card win-rate views
- opponent-side card win-rate views
- day-based card performance views using `battle_manifest.day`

This should let a user answer questions such as:

- which cards are strongest on day 3
- which opponent cards overperform on later days
- whether the same card behaves differently on player and opponent sides

## Risks

The main risks are:

- snapshot shape differences across payload versions
- inflated table size if mirrored data becomes very large
- incorrect opponent-side win logic if result inversion is forgotten in one query
- sparse card names causing more skipped rows than expected

These are manageable because:

- the source payload already stores snapshot cards
- the first version keeps only three identity fields
- focused tests can lock the side-aware aggregation rules

## Verification

Verification should stay proportional to the change and remain inside the Python toolchain.

Required:

- extend `tools/local_r2_clone/tests/test_local_r2_clone.py`
  - verify card rows are projected from battle snapshots
  - verify normalization for missing enchant and tier
  - verify rebuild replaces prior card rows for the same object
- extend `tools/local_r2_clone/tests/test_render_battles_report.py`
  - verify the HTML includes the new player/opponent card sections
  - verify day-based card analytics are rendered from projected metadata
  - verify opponent-side win rates use inverted result semantics

Not required:

- full repo build
- game mod test projects
- Worker test suite unrelated to local clone tooling

## Execution Plan

1. Add failing local clone tests that describe the projected battle-card rows and side-aware win-rate output.
2. Extend `tools/local_r2_clone/sql/local_r2_clone_schema.sql` with `battle_replay_cards` and indexes.
3. Extend `tools/local_r2_clone/clone_tool.py` to parse snapshot groups and project card rows during rebuild/sync.
4. Extend `tools/local_r2_clone/render_battles_charts.py` with dedicated query helpers and new report sections.
5. Run the focused Python tests for local clone metadata and report rendering.
