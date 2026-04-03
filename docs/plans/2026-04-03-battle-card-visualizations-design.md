# Battle Card Visualizations Design

## Goal

Strengthen the local R2 card analytics report with higher-signal visualizations so the report can answer:

- how the most common cards perform across `battle_manifest.day`
- which cards dominate each day
- how card performance changes over time without forcing the user to read only tables

This design builds on the existing card analytics projection and report sections that already show:

- top player cards
- top opponent cards
- player card win rates by day
- opponent card win rates by day

## Background

The current report now has enough card-level metadata to compute richer visuals, but the presentation is still table-heavy.

That is useful for exact values, but it makes two high-value questions harder to scan:

1. "Which globally common cards trend up or down as days increase?"
2. "On a given day, which cards are strong and common?"

The user explicitly wants stronger trend and day-distribution views, and also wants both card selection modes:

- global top N cards across all days
- per-day top N cards for each individual day

## Scope

This design covers:

- new report sections in `tools/local_r2_clone/render_battles_charts.py`
- new SQL helpers for global-trend and per-day heatmap inputs
- presentation rules for player-side and opponent-side views
- focused Python test updates for the report renderer

## Non-Goals

This design does not cover:

- interactive filtering
- JavaScript-driven chart controls
- changing the card metadata schema
- changing the existing `battle_replay_cards` projection
- replacing the current tables entirely

## Design Summary

Add two visualization layers on top of the current tables:

1. **Global top-N card trend charts**
2. **Day-card heatmap sections**

Keep the existing tables as the numeric reference layer.

Also add a compact "day leaders" table to cover the second card-selection mode without overloading the primary trend charts.

## Detailed Design

### 1. Global Top-N Trend Charts

Add two new multi-series trend charts:

- `Top Player Cards Across Days`
- `Top Opponent Cards Across Days`

Selection rule:

- choose cards by overall deduped battle count across all days for that side
- default limit should stay modest, such as 5 or 6 cards

Series encoding:

- x-axis: `battle_day`
- y-axis: win rate percentage
- one series per card identity (`card_name + enchant + tier`)

Label format:

- use compact labels such as `Fiery Cutlass | Burning | Silver`

Reasoning:

- this view is for cross-day comparability
- using global top N prevents the chart from turning into a noisy per-day union of unrelated cards

### 2. Day-Card Heatmap Sections

Add two heatmap-like matrix sections:

- `Player Day-Card Heatmap`
- `Opponent Day-Card Heatmap`

Selection rule:

- use per-day top N cards
- each day contributes its own top cards by deduped battle count

Presentation:

- columns are `battle_day`
- rows are card identities
- cells show `win_rate%` and `battles`
- cell color intensity reflects win rate

Recommended color semantics:

- low win rate: pale/desaturated warm tone
- mid win rate: neutral light tone
- high win rate: deeper saturated green tone
- no data: muted empty cell

Reasoning:

- the heatmap is the right place for the per-day top N mode
- it answers "what mattered on day 4?" better than a trend line does

### 3. Day Leaders Tables

Keep one additional table layer to make the heatmap auditable:

- `Player Day Leaders`
- `Opponent Day Leaders`

Columns:

- `Day`
- `Card`
- `Enchant`
- `Tier`
- `Battles`
- `Occurrences`
- `Wins`
- `Win Rate %`

This is the exact-value companion for the heatmap and prevents the visual layer from becoming the only way to inspect results.

### 4. Rendering Approach

Use the existing static HTML and SVG style.

Implementation approach:

- extend the existing SVG helpers with a multi-series line chart helper
- add a dedicated heatmap renderer that emits an HTML table with per-cell background colors

Do not bring in new chart libraries.

This keeps:

- the report single-file
- output deterministic
- styling consistent with the current handcrafted dashboard

### 5. Query Shape

Add separate helpers for the two selection modes.

#### Global top-N trend input

For each side:

- first compute overall top N card identities by deduped battle count
- then aggregate those identities by `battle_day`
- return rows shaped like:
  - `battle_day`
  - `card_label`
  - `deduped_battles`
  - `wins`
  - `win_rate_pct`

#### Per-day top-N heatmap input

For each side:

- aggregate by `battle_day + card identity`
- rank cards inside each day by deduped battle count, then win rate
- keep top N rows per day
- union the resulting card identities into the heatmap row set

Return enough information for each cell to show:

- `win_rate_pct`
- `deduped_battles`

### 6. Layout Strategy

To avoid making the page unwieldy:

- place the new trend charts above the card tables
- place the heatmap sections after the trend charts
- keep the existing top-card and day tables below as reference material

Recommended order:

1. overview KPIs
2. existing battle-level charts
3. player and opponent trend charts
4. player and opponent heatmaps
5. player and opponent top-card tables
6. player and opponent day-leader tables
7. existing matchup and size sections

### 7. Testing

Extend `tools/local_r2_clone/tests/test_render_battles_report.py` to assert:

- both new trend chart titles render
- both new heatmap section titles render
- globally selected cards appear in the trend sections
- per-day selected cards appear in the heatmap/day-leader sections
- representative values such as `50.00` and day labels still appear

No new metadata tests are required because the schema and projection are unchanged.

## Expected Outcome

After this change, the report should let a user scan three complementary views:

- **trend**: how globally important cards change across days
- **distribution**: which cards matter on each specific day
- **reference**: exact table values for the same cards

This should make the report feel more like an analysis dashboard and less like a set of exported tables.
