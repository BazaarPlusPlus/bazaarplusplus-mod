# Battle Card Analytics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local SQLite card-level battle metadata and extend the generated local R2 HTML report with player-side and opponent-side card win-rate analytics grouped by `battle_manifest.day`.

**Architecture:** Keep `battle_replays` as the battle-level fact table and add a new denormalized `battle_replay_cards` detail table populated from `battle_manifest.snapshots`. Build all card analytics from SQL over that detail table, using battle-deduped card presence for win-rate denominators and raw row counts for slot-occurrence totals.

**Tech Stack:** Python 3, SQLite, `unittest`, static HTML generation

---

### Task 1: Lock Card Projection Requirements With Failing Tests

**Files:**
- Modify: `tools/local_r2_clone/tests/test_local_r2_clone.py`

- [ ] **Step 1: Extend the schema-layout test to expect the new table**

Update `test_ensure_workspace_creates_schema_and_runtime_layout` so the schema probe includes `battle_replay_cards`:

```python
WHERE name IN (
  'sync_runs',
  'sync_run_errors',
  'r2_objects',
  'battle_replays',
  'battle_replay_cards',
  'run_summaries',
  'run_summaries_latest'
)
```

and add:

```python
self.assertEqual(objects["battle_replay_cards"], "table")
```

- [ ] **Step 2: Add a regression test for snapshot card projection and normalization**

Add a new test in `tools/local_r2_clone/tests/test_local_r2_clone.py` that seeds one battle replay with real snapshot groups:

```python
write_json(
    self.paths.mirror_dir / "battle-replays/client-1/battle-1/hash-a.json",
    {
        "battle_id": "battle-1",
        "run_id": "run-1",
        "schema_version": 3,
        "battle_manifest": {
            "battle_id": "battle-1",
            "run_id": "run-1",
            "recorded_at_utc": "2026-04-01T01:00:00.000Z",
            "day": 4,
            "hour": 1,
            "combat_kind": "PVPCombat",
            "participants": {
                "player_name": "Alpha",
                "player_account_id": "player-a",
                "opponent_name": "Beta",
                "opponent_account_id": "player-b",
            },
            "outcome": {
                "result": "win",
                "winner_combatant_id": "Player",
                "loser_combatant_id": "Opponent",
            },
            "snapshots": {
                "player_hand": {
                    "items": [
                        {"name": "Fiery Cutlass", "enchant": "Burning", "tier": "Silver"},
                        {"name": "Fiery Cutlass", "tier": "Silver"},
                        {"name": "Spare Dagger"},
                    ]
                },
                "player_skills": {
                    "items": [
                        {"name": "Quick Thinking", "enchant": "", "tier": "Gold"}
                    ]
                },
                "opponent_hand": {
                    "items": [
                        {"name": "Shield Wall", "enchant": "Heavy", "tier": "Bronze"}
                    ]
                },
                "opponent_skills": {
                    "items": [
                        {"tier": "Silver"}
                    ]
                },
            },
        },
        "replay_payload": {
            "battle_id": "battle-1",
            "version": 7,
            "spawn_message_base64": "a",
            "combat_message_base64": "b",
            "despawn_message_base64": "c",
        },
    },
)
```

After `rebuild_metadata(self.paths)`, query:

```python
rows = connection.execute(
    """
    SELECT side, card_group, card_name, enchant, tier, slot_index, battle_card_key, result, battle_day
    FROM battle_replay_cards
    ORDER BY side, card_group, slot_index
    """
).fetchall()
```

Assert:

- 5 rows are projected
- blank or missing `enchant` becomes `None`
- missing `tier` becomes `Unknown`
- the opponent skill entry with no `name` is skipped
- duplicate `Fiery Cutlass` rows keep different `slot_index` values but share the same `battle_card_key`
- all rows carry `battle_day = 4` and `result = "win"`

- [ ] **Step 3: Add a regression test for reprocessing the same object key**

Add a second test that:

1. writes a battle replay with two projected cards
2. runs `rebuild_metadata(self.paths)`
3. overwrites the same file path with a new payload containing one different card
4. runs `rebuild_metadata(self.paths)` again

After the second rebuild, assert:

- `SELECT COUNT(*) FROM battle_replay_cards WHERE object_key = ?` returns `1`
- old card names from the first version no longer exist for that `object_key`

- [ ] **Step 4: Run the focused projection tests to verify they fail**

Run:

```bash
python3 -m unittest tools.local_r2_clone.tests.test_local_r2_clone
```

Expected: FAIL because the current schema has no `battle_replay_cards` table and `clone_tool.py` does not project snapshot cards.

- [ ] **Step 5: Commit the failing test changes**

Run:

```bash
git add tools/local_r2_clone/tests/test_local_r2_clone.py
git commit -m "Add battle card projection tests"
```

### Task 2: Implement Card Projection In The Local Clone Metadata Pipeline

**Files:**
- Modify: `tools/local_r2_clone/sql/local_r2_clone_schema.sql`
- Modify: `tools/local_r2_clone/clone_tool.py`

- [ ] **Step 1: Extend the SQLite schema**

Add the new table and indexes to `tools/local_r2_clone/sql/local_r2_clone_schema.sql`:

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

CREATE INDEX IF NOT EXISTS idx_battle_replay_cards_side_day
  ON battle_replay_cards(side, battle_day);

CREATE INDEX IF NOT EXISTS idx_battle_replay_cards_card_lookup
  ON battle_replay_cards(side, card_name, enchant, tier);

CREATE INDEX IF NOT EXISTS idx_battle_replay_cards_battle
  ON battle_replay_cards(battle_id);
```

- [ ] **Step 2: Add snapshot parsing helpers in `clone_tool.py`**

Add compact helpers near the other parsing helpers:

```python
def expect_list(value: Any, field_name: str) -> list[Any]:
    if isinstance(value, list):
        return value
    raise ValueError(f"{field_name} is required")


def build_snapshot_rows(
    battle_id: str,
    recorded_at_utc: str,
    battle_day: int | None,
    result: str | None,
    snapshots: dict[str, Any],
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for side, card_group, snapshot_key in (
        ("player", "hand", "player_hand"),
        ("player", "skills", "player_skills"),
        ("opponent", "hand", "opponent_hand"),
        ("opponent", "skills", "opponent_skills"),
    ):
        capture = snapshots.get(snapshot_key)
        if not isinstance(capture, dict):
            continue
        items = capture.get("items")
        if not isinstance(items, list):
            continue
        for slot_index, item in enumerate(items):
            if not isinstance(item, dict):
                continue
            card_name = text_or_none(item.get("name"))
            if card_name is None:
                continue
            enchant = text_or_none(item.get("enchant")) or "None"
            tier = text_or_none(item.get("tier")) or "Unknown"
            rows.append(
                {
                    "battle_id": battle_id,
                    "battle_day": battle_day,
                    "recorded_at_utc": recorded_at_utc,
                    "side": side,
                    "card_group": card_group,
                    "result": result,
                    "card_name": card_name,
                    "enchant": enchant,
                    "tier": tier,
                    "slot_index": slot_index,
                    "battle_card_key": "|".join(
                        (battle_id, side, card_name, enchant, tier)
                    ),
                }
            )
    return rows
```

- [ ] **Step 3: Project card rows during battle replay upsert**

Refactor `upsert_battle_replay(...)` so it also reads `battle_manifest.snapshots`, then:

```python
connection.execute(
    "DELETE FROM battle_replay_cards WHERE object_key = ?",
    (object_key,),
)
for row in build_snapshot_rows(
    battle_id,
    recorded_at_utc,
    number_or_none(manifest.get("day")),
    text_or_none(outcome.get("result")),
    expect_mapping(manifest.get("snapshots") or {}, "battle_manifest.snapshots"),
):
    connection.execute(
        """
        INSERT INTO battle_replay_cards (
          object_key,
          battle_id,
          battle_day,
          recorded_at_utc,
          side,
          card_group,
          result,
          card_name,
          enchant,
          tier,
          slot_index,
          battle_card_key
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            object_key,
            row["battle_id"],
            row["battle_day"],
            row["recorded_at_utc"],
            row["side"],
            row["card_group"],
            row["result"],
            row["card_name"],
            row["enchant"],
            row["tier"],
            row["slot_index"],
            row["battle_card_key"],
        ),
    )
```

Keep the existing `battle_replays` upsert intact and perform the card insert after the parent row is written.

- [ ] **Step 4: Remove stale card rows on parse failure**

In the `except` block inside `process_object(...)`, add:

```python
connection.execute("DELETE FROM battle_replay_cards WHERE object_key = ?", (object_key,))
```

This should sit alongside the existing cleanup for `battle_replays` and `run_summaries`.

- [ ] **Step 5: Run the projection tests to verify they pass**

Run:

```bash
python3 -m unittest tools.local_r2_clone.tests.test_local_r2_clone
```

Expected: PASS

- [ ] **Step 6: Commit the projection implementation**

Run:

```bash
git add tools/local_r2_clone/sql/local_r2_clone_schema.sql tools/local_r2_clone/clone_tool.py tools/local_r2_clone/tests/test_local_r2_clone.py
git commit -m "Project battle cards into local clone metadata"
```

### Task 3: Lock The HTML Card Analytics Output With Failing Tests

**Files:**
- Modify: `tools/local_r2_clone/tests/test_render_battles_report.py`

- [ ] **Step 1: Replace the current empty snapshot payloads with real card snapshots**

Update `seed_projection()` so each battle uses snake_case snapshot groups and item arrays, for example:

```python
"snapshots": {
    "player_hand": {
        "items": [
            {"name": "Fiery Cutlass", "enchant": "Burning", "tier": "Silver"},
            {"name": "Fiery Cutlass", "tier": "Silver"},
        ]
    },
    "player_skills": {
        "items": [{"name": "Quick Thinking", "tier": "Gold"}]
    },
    "opponent_hand": {
        "items": [{"name": "Shield Wall", "enchant": "Heavy", "tier": "Bronze"}]
    },
    "opponent_skills": {
        "items": [{"name": "Emergency Repairs", "tier": "Silver"}]
    },
},
```

Use at least two battles on the same `day` with opposite results so one player-side card lands at `50.00%` and one opponent-side card lands at `50.00%` after inversion.

- [ ] **Step 2: Add assertions for the new card sections**

Extend `test_render_battles_report_writes_html_dashboard` so the rendered HTML contains:

```python
self.assertIn("Top Player Cards", html)
self.assertIn("Top Opponent Cards", html)
self.assertIn("Player Card Win Rates By Day", html)
self.assertIn("Opponent Card Win Rates By Day", html)
self.assertIn("Fiery Cutlass", html)
self.assertIn("Shield Wall", html)
self.assertIn("Burning", html)
self.assertIn("Silver", html)
```

- [ ] **Step 3: Add a focused regression test for side-aware win-rate semantics**

Add a new test that asserts the HTML contains all of:

```python
self.assertIn("Player Card Win Rates By Day", html)
self.assertIn("Opponent Card Win Rates By Day", html)
self.assertIn(">4<", html)
self.assertIn("50.00", html)
```

and at least one matchup string unique to each side-specific card section, for example:

```python
self.assertIn("Fiery Cutlass", html)
self.assertIn("Shield Wall", html)
```

The intent is to lock:

- `battle_day = 4` rendering
- player-side deduped card battles
- opponent-side inverted win semantics

- [ ] **Step 4: Run the report tests to verify they fail**

Run:

```bash
python3 -m unittest tools.local_r2_clone.tests.test_render_battles_report
```

Expected: FAIL because the current report does not query `battle_replay_cards` and has no card analytics sections.

- [ ] **Step 5: Commit the failing report tests**

Run:

```bash
git add tools/local_r2_clone/tests/test_render_battles_report.py
git commit -m "Add battle card report tests"
```

### Task 4: Render Card Analytics From Projected Metadata

**Files:**
- Modify: `tools/local_r2_clone/render_battles_charts.py`

- [ ] **Step 1: Add a reusable side-aware win expression helper**

Add a small helper near the existing query helpers:

```python
def side_win_case(side: str) -> str:
    if side == "player":
        return "CASE WHEN result = 'win' THEN 1 ELSE 0 END"
    if side == "opponent":
        return "CASE WHEN result = 'loss' THEN 1 ELSE 0 END"
    raise ValueError(f"Unsupported side: {side}")
```

- [ ] **Step 2: Add summary card queries**

Add:

```python
def query_top_cards(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit: int = 12,
) -> list[tuple[str, str, str, int, int, int, float]]:
```

with SQL shaped like:

```sql
WITH card_battles AS (
  SELECT
    side,
    battle_day,
    card_name,
    enchant,
    tier,
    battle_card_key,
    MAX(CASE WHEN result IN ('win', 'loss') THEN 1 ELSE 0 END) AS has_decision,
    MAX(<SIDE_WIN_CASE>) AS win_flag,
    COUNT(*) AS occurrences_in_battle
  FROM battle_replay_cards
  WHERE side = ?
  GROUP BY side, battle_day, card_name, enchant, tier, battle_card_key
)
SELECT
  card_name,
  enchant,
  tier,
  COUNT(DISTINCT battle_day) AS days_seen,
  COUNT(*) AS deduped_battles,
  SUM(occurrences_in_battle) AS occurrences,
  SUM(CASE WHEN has_decision = 1 THEN win_flag ELSE 0 END) AS wins,
  ROUND(
    100.0 * SUM(CASE WHEN has_decision = 1 THEN win_flag ELSE 0 END)
    / NULLIF(SUM(CASE WHEN has_decision = 1 THEN 1 ELSE 0 END), 0),
    2
  ) AS win_rate_pct
FROM card_battles
GROUP BY card_name, enchant, tier
ORDER BY deduped_battles DESC, win_rate_pct DESC, card_name ASC
LIMIT ?
```

- [ ] **Step 3: Add day-breakdown card queries**

Add:

```python
def query_card_win_rates_by_day(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit_per_day: int = 8,
) -> list[tuple[int, str, str, str, int, int, int, float]]:
```

Use a windowed query:

```sql
WITH card_battles AS (
  SELECT
    battle_day,
    card_name,
    enchant,
    tier,
    battle_card_key,
    MAX(CASE WHEN result IN ('win', 'loss') THEN 1 ELSE 0 END) AS has_decision,
    MAX(<SIDE_WIN_CASE>) AS win_flag,
    COUNT(*) AS occurrences_in_battle
  FROM battle_replay_cards
  WHERE side = ?
    AND battle_day IS NOT NULL
  GROUP BY battle_day, card_name, enchant, tier, battle_card_key
),
aggregated AS (
  SELECT
    battle_day,
    card_name,
    enchant,
    tier,
    COUNT(*) AS deduped_battles,
    SUM(occurrences_in_battle) AS occurrences,
    SUM(CASE WHEN has_decision = 1 THEN win_flag ELSE 0 END) AS wins,
    ROUND(
      100.0 * SUM(CASE WHEN has_decision = 1 THEN win_flag ELSE 0 END)
      / NULLIF(SUM(CASE WHEN has_decision = 1 THEN 1 ELSE 0 END), 0),
      2
    ) AS win_rate_pct,
    ROW_NUMBER() OVER (
      PARTITION BY battle_day
      ORDER BY COUNT(*) DESC, win_rate_pct DESC, card_name ASC
    ) AS row_num
  FROM card_battles
  GROUP BY battle_day, card_name, enchant, tier
)
SELECT battle_day, card_name, enchant, tier, deduped_battles, occurrences, wins, win_rate_pct
FROM aggregated
WHERE row_num <= ?
ORDER BY battle_day ASC, deduped_battles DESC, win_rate_pct DESC, card_name ASC
```

- [ ] **Step 4: Add the new report sections**

Extend `build_report_html(...)` with:

```python
top_player_cards = query_top_cards(connection, side="player")
top_opponent_cards = query_top_cards(connection, side="opponent")
player_cards_by_day = query_card_win_rates_by_day(connection, side="player")
opponent_cards_by_day = query_card_win_rates_by_day(connection, side="opponent")
```

and add four table sections:

```python
render_table(
    "Top Player Cards",
    ("Card", "Enchant", "Tier", "Days Seen", "Battles", "Occurrences", "Wins", "Win Rate %"),
    ((name, enchant, tier, days_seen, battles, occurrences, wins, f"{rate:.2f}") for name, enchant, tier, days_seen, battles, occurrences, wins, rate in top_player_cards),
),
render_table(
    "Top Opponent Cards",
    ("Card", "Enchant", "Tier", "Days Seen", "Battles", "Occurrences", "Wins", "Win Rate %"),
    ((name, enchant, tier, days_seen, battles, occurrences, wins, f"{rate:.2f}") for name, enchant, tier, days_seen, battles, occurrences, wins, rate in top_opponent_cards),
),
render_table(
    "Player Card Win Rates By Day",
    ("Day", "Card", "Enchant", "Tier", "Battles", "Occurrences", "Wins", "Win Rate %"),
    ((day, name, enchant, tier, battles, occurrences, wins, f"{rate:.2f}") for day, name, enchant, tier, battles, occurrences, wins, rate in player_cards_by_day),
),
render_table(
    "Opponent Card Win Rates By Day",
    ("Day", "Card", "Enchant", "Tier", "Battles", "Occurrences", "Wins", "Win Rate %"),
    ((day, name, enchant, tier, battles, occurrences, wins, f"{rate:.2f}") for day, name, enchant, tier, battles, occurrences, wins, rate in opponent_cards_by_day),
),
```

- [ ] **Step 5: Run the report tests to verify they pass**

Run:

```bash
python3 -m unittest tools.local_r2_clone.tests.test_render_battles_report
```

Expected: PASS

- [ ] **Step 6: Commit the report implementation**

Run:

```bash
git add tools/local_r2_clone/render_battles_charts.py tools/local_r2_clone/tests/test_render_battles_report.py
git commit -m "Render battle card analytics report"
```

### Task 5: Run Focused End-To-End Verification

**Files:**
- Reuse files above

- [ ] **Step 1: Run the focused local clone test suite**

Run:

```bash
python3 -m unittest \
  tools.local_r2_clone.tests.test_local_r2_clone \
  tools.local_r2_clone.tests.test_render_battles_report
```

Expected: PASS

- [ ] **Step 2: Rebuild local metadata from the tool workspace**

Run:

```bash
python3 tools/local_r2_clone/rebuild_local_r2_metadata.py --base-dir tools/local_r2_clone
```

Expected: exits successfully and refreshes `tools/local_r2_clone/runtime/meta/clone.db`

- [ ] **Step 3: Render the HTML report from the rebuilt metadata**

Run:

```bash
python3 tools/local_r2_clone/render_battles_charts.py --base-dir tools/local_r2_clone
```

Expected: prints `tools/local_r2_clone/runtime/reports/index.html` and the file contains the new card analytics sections.

- [ ] **Step 4: Commit the final verified state**

Run:

```bash
git add tools/local_r2_clone/sql/local_r2_clone_schema.sql tools/local_r2_clone/clone_tool.py tools/local_r2_clone/render_battles_charts.py tools/local_r2_clone/tests/test_local_r2_clone.py tools/local_r2_clone/tests/test_render_battles_report.py
git commit -m "Add battle card analytics to local clone report"
```
