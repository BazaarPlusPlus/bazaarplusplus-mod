# Hero Ten-Win Rate Design

## Goal

Extend the local R2 analytics report with a run-level hero metric that answers:

- for each hero, how often completed runs reach the game's maximum of 10 wins

The user explicitly clarified the intended semantics:

- the metric is run-level, not battle-level
- only `completed` runs should count in the denominator
- 10 wins means the run reached the game's win cap, so `final_wins >= 10` counts as success

## Background

The report already mixes:

- battle-level analytics from `battle_replays`
- run-level metadata from `run_summaries_latest`

However, it currently has no hero-level run outcome metric comparable to a "top finish rate". Adding a 10-win rate fills that gap and complements the existing battle-side hero win-rate charts.

## Scope

This design covers:

- run-level SQL over `run_summaries_latest`
- one new hero 10-win rate chart
- one new details table
- focused report test coverage

## Non-Goals

This design does not cover:

- changing run summary projection
- abandoned-run analysis
- final wins distribution histograms
- hero MMR trend analysis

## Design Summary

Add one run-level metric section with two views:

1. `Hero 10-Win Rates` bar chart
2. `Hero 10-Win Rate Details` table

This keeps the metric visible at a glance while preserving exact counts for interpretation.

## Metric Definition

### Population

Use only rows from `run_summaries_latest` where:

- `status = 'completed'`
- `hero_name IS NOT NULL`

Each row represents the latest terminal summary for a run.

### Success Condition

A run counts as a 10-win success when:

- `final_wins >= 10`

Even if the current game rules cap wins at exactly 10, using `>= 10` is the safer interpretation and handles any legacy or future payload oddities without undercounting.

### Measures

For each `hero_name`, compute:

- `completed_runs`
- `ten_win_runs`
- `ten_win_rate_pct`

where:

- `completed_runs = COUNT(*)`
- `ten_win_runs = SUM(CASE WHEN final_wins >= 10 THEN 1 ELSE 0 END)`
- `ten_win_rate_pct = 100.0 * ten_win_runs / completed_runs`

## Report Additions

### 1. Hero 10-Win Rates

Add a bar chart titled:

- `Hero 10-Win Rates`

Chart semantics:

- x-axis: hero name
- y-axis: 10-win rate percentage
- sort heroes by:
  - `ten_win_rate_pct DESC`
  - `completed_runs DESC`
  - `hero_name ASC`

The chart should use a distinct color from the existing battle-level hero win-rate chart so users do not confuse the two metrics.

### 2. Hero 10-Win Rate Details

Add a table titled:

- `Hero 10-Win Rate Details`

Columns:

- `Hero`
- `Completed Runs`
- `10-Win Runs`
- `10-Win Rate %`

This table is the exact-value companion to the chart and makes it easy to spot small-sample heroes.

## Query Shape

Add one dedicated helper in `tools/local_r2_clone/render_battles_charts.py`, for example:

```python
def query_hero_ten_win_rates(connection: sqlite3.Connection) -> list[tuple[str, int, int, float]]:
```

Recommended SQL:

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

## Layout

Place the new run-level hero metric near the existing hero and rank summary area, before the card analytics sections.

Recommended order:

1. battle overview KPIs
2. daily and hourly battle charts
3. battle-level hero win rates
4. rank win rates
5. run-level `Hero 10-Win Rates`
6. run-level `Hero 10-Win Rate Details`
7. card analytics and matchup sections

This keeps hero-related metrics grouped together while preserving the distinction between battle-level and run-level views.

## Testing

Extend `tools/local_r2_clone/tests/test_render_battles_report.py` to seed run summaries with at least:

- one hero with a completed 10-win run
- one hero with completed runs that do not reach 10 wins
- one non-completed run that should be excluded

Assertions should verify:

- the new chart title renders
- the details table title renders
- the expected hero names appear
- a hero with one 10-win completion out of one completed run shows `100.00`
- an unfinished or non-completed run does not affect the denominator

## Expected Outcome

After this change, the report will show a hero-level top-finish metric that complements the existing battle win-rate sections and gives a clearer answer to "which heroes actually convert completed runs into a 10-win finish?"
