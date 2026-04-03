# Hero Ten-Win Rate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a run-level hero 10-win rate chart and details table to the local R2 analytics report using completed runs from `run_summaries_latest`.

**Architecture:** Keep the change entirely inside the report-render layer. Seed completed and non-completed run summaries in the report test fixture, add a dedicated SQL helper over `run_summaries_latest`, then render one bar chart and one table alongside the existing hero and rank sections.

**Tech Stack:** Python 3, SQLite, static HTML, SVG, `unittest`

---

### Task 1: Add Failing Report Tests For Hero 10-Win Rates

**Files:**
- Modify: `tools/local_r2_clone/tests/test_render_battles_report.py`

- [ ] **Step 1: Seed completed and non-completed run summaries**

Extend `seed_projection()` in `tools/local_r2_clone/tests/test_render_battles_report.py` with:

```python
write_json(
    self.paths.mirror_dir / "run-summaries/client-1/run-10/hash-a.json",
    {
        "run_id": "run-10",
        "status": "completed",
        "hero_id": "hero-vanessa",
        "hero_name": "Vanessa",
        "started_at_utc": "2026-04-01T00:00:00.000Z",
        "ended_at_utc": "2026-04-01T01:00:00.000Z",
        "final_day": 10,
        "final_wins": 10,
        "final_losses": 1,
        "mmr": 1300,
        "schema_version": 2,
    },
)
write_json(
    self.paths.mirror_dir / "run-summaries/client-1/run-11/hash-b.json",
    {
        "run_id": "run-11",
        "status": "completed",
        "hero_id": "hero-dooley",
        "hero_name": "Dooley",
        "started_at_utc": "2026-04-02T00:00:00.000Z",
        "ended_at_utc": "2026-04-02T01:00:00.000Z",
        "final_day": 8,
        "final_wins": 8,
        "final_losses": 2,
        "mmr": 1210,
        "schema_version": 2,
    },
)
write_json(
    self.paths.mirror_dir / "run-summaries/client-1/run-12/hash-c.json",
    {
        "run_id": "run-12",
        "status": "active",
        "hero_id": "hero-vanessa",
        "hero_name": "Vanessa",
        "started_at_utc": "2026-04-03T00:00:00.000Z",
        "ended_at_utc": "2026-04-03T00:30:00.000Z",
        "final_day": 7,
        "final_wins": 7,
        "final_losses": 1,
        "mmr": 1250,
        "schema_version": 2,
    },
)
```

- [ ] **Step 2: Add assertions for the new report sections**

Add a new test:

```python
def test_render_battles_report_includes_hero_ten_win_rate_sections(self) -> None:
    self.seed_projection()

    report_path = render_battles_report(self.paths)
    html = report_path.read_text(encoding="utf-8")

    self.assertIn("Hero 10-Win Rates", html)
    self.assertIn("Hero 10-Win Rate Details", html)
    self.assertIn("Vanessa", html)
    self.assertIn("Dooley", html)
    self.assertIn("100.00", html)
```

- [ ] **Step 3: Add a regression test for completed-only denominator semantics**

Add another test:

```python
def test_render_battles_report_uses_completed_runs_for_hero_ten_win_rates(self) -> None:
    self.seed_projection()

    report_path = render_battles_report(self.paths)
    html = report_path.read_text(encoding="utf-8")

    self.assertIn("Hero 10-Win Rate Details", html)
    self.assertIn(">1<", html)
    self.assertIn(">0<", html)
```

The intent is:

- Vanessa has one completed run and one active run
- only the completed run counts
- Dooley has one completed non-10-win run

- [ ] **Step 4: Run the focused report tests to verify they fail**

Run:

```bash
python3 -m unittest tools.local_r2_clone.tests.test_render_battles_report
```

Expected: FAIL because the current report has no hero 10-win sections.

### Task 2: Implement Hero 10-Win Rate Query And Rendering

**Files:**
- Modify: `tools/local_r2_clone/render_battles_charts.py`

- [ ] **Step 1: Add the run-level query helper**

Add:

```python
def query_hero_ten_win_rates(connection: sqlite3.Connection) -> list[tuple[str, int, int, float]]:
```

with SQL:

```sql
SELECT
  hero_name,
  COUNT(*) AS completed_runs,
  SUM(CASE WHEN COALESCE(final_wins, 0) >= 10 THEN 1 ELSE 0 END) AS ten_win_runs,
  ROUND(
    100.0 * SUM(CASE WHEN COALESCE(final_wins, 0) >= 10 THEN 1 ELSE 0 END) / COUNT(*),
    2
  ) AS ten_win_rate_pct
FROM run_summaries_latest
WHERE status = 'completed'
  AND hero_name IS NOT NULL
GROUP BY hero_name
ORDER BY ten_win_rate_pct DESC, completed_runs DESC, hero_name ASC
LIMIT 8
```

- [ ] **Step 2: Add the new chart and table sections**

In `build_report_html(...)`, add:

```python
hero_ten_win_rates = query_hero_ten_win_rates(connection)
```

Then render:

```python
render_chart_section(
    "Hero 10-Win Rates",
    "Completed-run rate of reaching the 10-win cap for each hero.",
    render_bar_chart(
        "Hero 10-Win Rates",
        [(hero, rate) for hero, _, _, rate in hero_ten_win_rates],
        value_formatter="{value}%",
        color="#0f766e",
    ),
),
render_table(
    "Hero 10-Win Rate Details",
    ("Hero", "Completed Runs", "10-Win Runs", "10-Win Rate %"),
    ((hero, completed_runs, ten_win_runs, f"{rate:.2f}") for hero, completed_runs, ten_win_runs, rate in hero_ten_win_rates),
),
```

Place these sections after the existing `Rank Win Rate Details` section and before the card analytics sections.

- [ ] **Step 3: Run the focused report tests to verify they pass**

Run:

```bash
python3 -m unittest tools.local_r2_clone.tests.test_render_battles_report
```

Expected: PASS

### Task 3: Run Focused Verification

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
