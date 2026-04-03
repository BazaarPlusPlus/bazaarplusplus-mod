# Battle Card Visualizations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Strengthen the local R2 card analytics dashboard with cross-day trend charts, day-card heatmaps, and day-leader tables for player-side and opponent-side cards.

**Architecture:** Keep the existing `battle_replay_cards` metadata as the source of truth and extend only the report-render layer. Add one query path for globally selected top-N trend series, one query path for per-day top-N heatmap inputs, then render both using handcrafted SVG/HTML helpers that match the current static dashboard style.

**Tech Stack:** Python 3, SQLite, static HTML, SVG, `unittest`

---

### Task 1: Add Failing Report Tests For Trend Charts And Heatmaps

**Files:**
- Modify: `tools/local_r2_clone/tests/test_render_battles_report.py`

- [ ] **Step 1: Extend the seeded report dataset with extra cards for day-level differentiation**

Update `seed_projection()` in `tools/local_r2_clone/tests/test_render_battles_report.py` so the three existing battles still cover:

- repeated player-side `Fiery Cutlass | Burning | Silver` on day 4
- repeated opponent-side `Shield Wall | Heavy | Bronze` on day 4
- different player and opponent cards on day 7

and add one more battle on day 7 so the trend charts have at least two data points for a recurring card:

```python
write_json(
    self.paths.mirror_dir / "battle-replays/client-3/battle-4/hash-d.json",
    {
        "battle_id": "battle-4",
        "run_id": "run-3",
        "schema_version": 3,
        "battle_manifest": {
            "battle_id": "battle-4",
            "run_id": "run-3",
            "recorded_at_utc": "2026-04-02T09:15:00.000Z",
            "day": 7,
            "hour": 9,
            "combat_kind": "PVPCombat",
            "participants": {
                "player_name": "Echo",
                "player_account_id": "player-e",
                "player_hero": "Vanessa",
                "player_rank": "Silver",
                "player_rating": 1125,
                "player_level": 6,
                "opponent_name": "Foxtrot",
                "opponent_account_id": "player-f",
                "opponent_hero": "Dooley",
                "opponent_rank": "Silver",
                "opponent_rating": 1105,
                "opponent_level": 6,
            },
            "outcome": {
                "result": "loss",
                "winner_combatant_id": "Opponent",
                "loser_combatant_id": "Player",
            },
            "snapshots": {
                "player_hand": {
                    "items": [
                        {"name": "Fiery Cutlass", "enchant": "Burning", "tier": "Silver"},
                        {"name": "Storm Lantern", "tier": "Gold"},
                    ]
                },
                "player_skills": {
                    "items": [{"name": "Quick Thinking", "tier": "Gold"}]
                },
                "opponent_hand": {
                    "items": [
                        {"name": "Shield Wall", "enchant": "Heavy", "tier": "Bronze"},
                        {"name": "Steam Lance", "tier": "Bronze"},
                    ]
                },
                "opponent_skills": {
                    "items": [{"name": "Emergency Repairs", "tier": "Silver"}]
                },
            },
        },
        "replay_payload": {
            "battle_id": "battle-4",
            "version": 7,
            "spawn_message_base64": "aaaa",
            "combat_message_base64": "bbbb",
            "despawn_message_base64": "cccc",
        },
    },
)
```

- [ ] **Step 2: Add a regression test for the new trend sections**

Add a test in `tools/local_r2_clone/tests/test_render_battles_report.py`:

```python
def test_render_battles_report_includes_global_top_card_trend_sections(self) -> None:
    self.seed_projection()

    report_path = render_battles_report(self.paths)
    html = report_path.read_text(encoding="utf-8")

    self.assertIn("Top Player Cards Across Days", html)
    self.assertIn("Top Opponent Cards Across Days", html)
    self.assertIn("Fiery Cutlass | Burning | Silver", html)
    self.assertIn("Shield Wall | Heavy | Bronze", html)
    self.assertIn("Day 4", html)
    self.assertIn("Day 7", html)
```

- [ ] **Step 3: Add a regression test for the new heatmap and day-leader sections**

Add another test:

```python
def test_render_battles_report_includes_day_card_heatmaps_and_day_leaders(self) -> None:
    self.seed_projection()

    report_path = render_battles_report(self.paths)
    html = report_path.read_text(encoding="utf-8")

    self.assertIn("Player Day-Card Heatmap", html)
    self.assertIn("Opponent Day-Card Heatmap", html)
    self.assertIn("Player Day Leaders", html)
    self.assertIn("Opponent Day Leaders", html)
    self.assertIn("Storm Lantern", html)
    self.assertIn("Steam Lance", html)
    self.assertIn("50.00", html)
```

- [ ] **Step 4: Run the focused report tests to verify they fail**

Run:

```bash
python3 -m unittest tools.local_r2_clone.tests.test_render_battles_report
```

Expected: FAIL because the current report has no trend-chart titles, no heatmap sections, and no day-leader tables.

- [ ] **Step 5: Commit the failing tests**

Run:

```bash
git add tools/local_r2_clone/tests/test_render_battles_report.py
git commit -m "Add battle card visualization tests"
```

### Task 2: Implement Global Top-N Trend Charts

**Files:**
- Modify: `tools/local_r2_clone/render_battles_charts.py`

- [ ] **Step 1: Add a query helper for globally selected card trends**

Add a helper in `tools/local_r2_clone/render_battles_charts.py`:

```python
def query_top_card_trends(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit: int = 5,
) -> list[tuple[int, str, int, int, float]]:
```

Use SQL shaped like:

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
    MAX(<SIDE_WIN_CASE>) AS win_flag
  FROM battle_replay_cards
  WHERE side = ?
    AND battle_day IS NOT NULL
  GROUP BY side, battle_day, card_name, enchant, tier, battle_card_key
),
top_cards AS (
  SELECT
    card_name,
    enchant,
    tier
  FROM card_battles
  GROUP BY card_name, enchant, tier
  ORDER BY COUNT(*) DESC, card_name ASC, enchant ASC, tier ASC
  LIMIT ?
)
SELECT
  cb.battle_day,
  cb.card_name || ' | ' || cb.enchant || ' | ' || cb.tier AS card_label,
  COUNT(*) AS deduped_battles,
  SUM(CASE WHEN cb.has_decision = 1 THEN cb.win_flag ELSE 0 END) AS wins,
  COALESCE(
    ROUND(
      100.0 * SUM(CASE WHEN cb.has_decision = 1 THEN cb.win_flag ELSE 0 END)
      / NULLIF(SUM(CASE WHEN cb.has_decision = 1 THEN 1 ELSE 0 END), 0),
      2
    ),
    0
  ) AS win_rate_pct
FROM card_battles cb
JOIN top_cards tc
  ON tc.card_name = cb.card_name
 AND tc.enchant = cb.enchant
 AND tc.tier = cb.tier
GROUP BY cb.battle_day, card_label
ORDER BY cb.battle_day ASC, card_label ASC
```

- [ ] **Step 2: Add a multi-series line chart renderer**

Add a new renderer in `tools/local_r2_clone/render_battles_charts.py`:

```python
def render_multi_series_line_chart(
    title: str,
    x_labels: Sequence[str],
    series: Sequence[tuple[str, Sequence[float | int]]],
    *,
    value_formatter: str = "{value}",
    width: int = 720,
    height: int = 360,
) -> str:
```

Implementation requirements:

- reuse the current SVG style vocabulary (`grid`, `axis-label`, `value-label`)
- draw one colored polyline per series
- place x-axis labels as `Day 4`, `Day 7`, etc.
- render a compact legend inside the SVG using the series label text
- if there is no series data, return `render_empty_chart(title)`

- [ ] **Step 3: Add report sections for player and opponent trend charts**

Extend `build_report_html(...)` with:

```python
player_trends = query_top_card_trends(connection, side="player")
opponent_trends = query_top_card_trends(connection, side="opponent")
```

Then transform each query result into:

- ordered day labels
- per-card numeric series for win rate percentage

and add:

```python
render_chart_section(
    "Top Player Cards Across Days",
    "Win-rate trends for the most common player-side cards across battle days.",
    render_multi_series_line_chart(...),
),
render_chart_section(
    "Top Opponent Cards Across Days",
    "Win-rate trends for the most common opponent-side cards across battle days.",
    render_multi_series_line_chart(...),
),
```

- [ ] **Step 4: Run the focused report tests to verify the trend tests pass**

Run:

```bash
python3 -m unittest tools.local_r2_clone.tests.test_render_battles_report
```

Expected: the new trend-section assertions pass, while the heatmap/day-leader assertions still fail.

- [ ] **Step 5: Commit the trend chart implementation**

Run:

```bash
git add tools/local_r2_clone/render_battles_charts.py tools/local_r2_clone/tests/test_render_battles_report.py
git commit -m "Render battle card trend charts"
```

### Task 3: Implement Day-Card Heatmaps And Day-Leader Tables

**Files:**
- Modify: `tools/local_r2_clone/render_battles_charts.py`

- [ ] **Step 1: Add a query helper for per-day top-N heatmap inputs**

Add:

```python
def query_day_card_heatmap(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit_per_day: int = 5,
) -> list[tuple[str, int, int, float]]:
```

Use SQL shaped like:

```sql
WITH card_battles AS (
  SELECT
    battle_day,
    card_name,
    enchant,
    tier,
    battle_card_key,
    MAX(CASE WHEN result IN ('win', 'loss') THEN 1 ELSE 0 END) AS has_decision,
    MAX(<SIDE_WIN_CASE>) AS win_flag
  FROM battle_replay_cards
  WHERE side = ?
    AND battle_day IS NOT NULL
  GROUP BY battle_day, card_name, enchant, tier, battle_card_key
),
aggregated AS (
  SELECT
    battle_day,
    card_name || ' | ' || enchant || ' | ' || tier AS card_label,
    COUNT(*) AS deduped_battles,
    SUM(CASE WHEN has_decision = 1 THEN win_flag ELSE 0 END) AS wins,
    COALESCE(
      ROUND(
        100.0 * SUM(CASE WHEN has_decision = 1 THEN win_flag ELSE 0 END)
        / NULLIF(SUM(CASE WHEN has_decision = 1 THEN 1 ELSE 0 END), 0),
        2
      ),
      0
    ) AS win_rate_pct
  FROM card_battles
  GROUP BY battle_day, card_label
),
ranked AS (
  SELECT
    battle_day,
    card_label,
    deduped_battles,
    win_rate_pct,
    ROW_NUMBER() OVER (
      PARTITION BY battle_day
      ORDER BY deduped_battles DESC, win_rate_pct DESC, card_label ASC
    ) AS row_num
  FROM aggregated
)
SELECT battle_day, card_label, deduped_battles, win_rate_pct
FROM ranked
WHERE row_num <= ?
ORDER BY battle_day ASC, deduped_battles DESC, win_rate_pct DESC, card_label ASC
```

- [ ] **Step 2: Add a heatmap renderer**

Add:

```python
def render_heatmap_section(
    title: str,
    description: str,
    rows: Sequence[str],
    columns: Sequence[str],
    values: dict[tuple[str, str], tuple[float, int]],
) -> str:
```

Implementation requirements:

- render a regular HTML table, not SVG
- first column is the card label
- each remaining cell corresponds to a `Day N` column
- populated cells show two lines:
  - `XX.XX%`
  - `<battles> battles`
- empty cells show `-`
- set inline background color from win rate:
  - near 0% = light warm tint
  - around 50% = neutral cream
  - near 100% = deeper green tint

- [ ] **Step 3: Add a query helper for day leaders**

Add:

```python
def query_day_leaders(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit_per_day: int = 5,
) -> list[tuple[int, str, str, str, int, int, int, float]]:
```

This can reuse the existing `query_card_win_rates_by_day(...)` shape with a lower default limit, or be a thin wrapper around it.

- [ ] **Step 4: Add heatmap and day-leader report sections**

Extend `build_report_html(...)` with:

```python
player_heatmap = query_day_card_heatmap(connection, side="player")
opponent_heatmap = query_day_card_heatmap(connection, side="opponent")
player_day_leaders = query_day_leaders(connection, side="player")
opponent_day_leaders = query_day_leaders(connection, side="opponent")
```

Transform the heatmap rows into:

- sorted `Day N` columns
- sorted card-label rows
- value lookup dictionary keyed by `(card_label, day_label)`

Then add:

```python
render_heatmap_section(
    "Player Day-Card Heatmap",
    "Per-day top player-side cards with win-rate intensity and deduped battle counts.",
    ...,
),
render_heatmap_section(
    "Opponent Day-Card Heatmap",
    "Per-day top opponent-side cards with win-rate intensity and deduped battle counts.",
    ...,
),
render_table(
    "Player Day Leaders",
    ("Day", "Card", "Enchant", "Tier", "Battles", "Occurrences", "Wins", "Win Rate %"),
    ((day, name, enchant, tier, battles, occurrences, wins, f"{rate:.2f}") for day, name, enchant, tier, battles, occurrences, wins, rate in player_day_leaders),
),
render_table(
    "Opponent Day Leaders",
    ("Day", "Card", "Enchant", "Tier", "Battles", "Occurrences", "Wins", "Win Rate %"),
    ((day, name, enchant, tier, battles, occurrences, wins, f"{rate:.2f}") for day, name, enchant, tier, battles, occurrences, wins, rate in opponent_day_leaders),
),
```

- [ ] **Step 5: Run the focused report tests to verify they pass**

Run:

```bash
python3 -m unittest tools.local_r2_clone.tests.test_render_battles_report
```

Expected: PASS

- [ ] **Step 6: Commit the heatmap and day-leader implementation**

Run:

```bash
git add tools/local_r2_clone/render_battles_charts.py tools/local_r2_clone/tests/test_render_battles_report.py
git commit -m "Render battle card heatmaps"
```

### Task 4: Run Focused Verification

**Files:**
- Reuse files above

- [ ] **Step 1: Run the full focused local clone test suite**

Run:

```bash
python3 -m unittest \
  tools.local_r2_clone.tests.test_local_r2_clone \
  tools.local_r2_clone.tests.test_render_battles_report
```

Expected: PASS

- [ ] **Step 2: Render the dashboard from the local tool database**

Run:

```bash
python3 tools/local_r2_clone/render_battles_charts.py --base-dir tools/local_r2_clone
```

Expected: prints `tools/local_r2_clone/runtime/reports/index.html`

- [ ] **Step 3: Commit the final verified state**

Run:

```bash
git add tools/local_r2_clone/render_battles_charts.py tools/local_r2_clone/tests/test_render_battles_report.py
git commit -m "Add battle card visualizations"
```
