from __future__ import annotations

import argparse
import html
import math
import sqlite3
from pathlib import Path
from typing import Iterable, Sequence

from tools.local_r2_clone.clone_tool import ClonePaths, DEFAULT_BASE_DIR, connect_db


def fetch_all(connection: sqlite3.Connection, sql: str, params: Sequence[object] = ()) -> list[sqlite3.Row]:
    return list(connection.execute(sql, params).fetchall())


def query_kpis(connection: sqlite3.Connection) -> dict[str, str]:
    row = connection.execute(
        """
        SELECT
          COUNT(*) AS total_battles,
          COUNT(DISTINCT player_account_id) AS unique_players,
          COUNT(DISTINCT opponent_account_id) AS unique_opponents,
          COALESCE(SUM(replay_size_bytes), 0) AS total_replay_bytes,
          MIN(recorded_at_utc) AS first_battle_utc,
          MAX(recorded_at_utc) AS last_battle_utc
        FROM battle_replays
        """
    ).fetchone()
    assert row is not None
    return {
        "Total Battles": str(row["total_battles"]),
        "Unique Players": str(row["unique_players"]),
        "Unique Opponents": str(row["unique_opponents"]),
        "Total Replay Size": human_bytes(int(row["total_replay_bytes"])),
        "First Battle": row["first_battle_utc"] or "-",
        "Last Battle": row["last_battle_utc"] or "-",
    }


def query_daily_battles(connection: sqlite3.Connection) -> list[tuple[str, int]]:
    return [
        (row["day_utc"], int(row["battle_count"]))
        for row in fetch_all(
            connection,
            """
            SELECT
              substr(recorded_at_utc, 1, 10) AS day_utc,
              COUNT(*) AS battle_count
            FROM battle_replays
            GROUP BY substr(recorded_at_utc, 1, 10)
            ORDER BY day_utc
            """,
        )
    ]


def query_hourly_battles(connection: sqlite3.Connection) -> list[tuple[str, int]]:
    rows = fetch_all(
        connection,
        """
        SELECT
          COALESCE(hour, CAST(substr(recorded_at_utc, 12, 2) AS INTEGER)) AS battle_hour,
          COUNT(*) AS battle_count
        FROM battle_replays
        GROUP BY COALESCE(hour, CAST(substr(recorded_at_utc, 12, 2) AS INTEGER))
        ORDER BY battle_hour
        """,
    )
    return [(f"{int(row['battle_hour']):02d}:00", int(row["battle_count"])) for row in rows]


def query_hero_win_rates(connection: sqlite3.Connection) -> list[tuple[str, int, float]]:
    return [
        (row["player_hero"], int(row["battle_count"]), float(row["win_rate_pct"]))
        for row in fetch_all(
            connection,
            """
            SELECT
              player_hero,
              COUNT(*) AS battle_count,
              ROUND(
                100.0 * SUM(CASE WHEN result = 'win' THEN 1 ELSE 0 END) / COUNT(*),
                2
              ) AS win_rate_pct
            FROM battle_replays
            WHERE player_hero IS NOT NULL
              AND result IN ('win', 'loss')
            GROUP BY player_hero
            ORDER BY battle_count DESC, win_rate_pct DESC, player_hero ASC
            LIMIT 8
            """,
        )
    ]


def query_rank_win_rates(connection: sqlite3.Connection) -> list[tuple[str, int, float]]:
    return [
        (row["player_rank"], int(row["battle_count"]), float(row["win_rate_pct"]))
        for row in fetch_all(
            connection,
            """
            SELECT
              player_rank,
              COUNT(*) AS battle_count,
              ROUND(
                100.0 * SUM(CASE WHEN result = 'win' THEN 1 ELSE 0 END) / COUNT(*),
                2
              ) AS win_rate_pct
            FROM battle_replays
            WHERE player_rank IS NOT NULL
              AND result IN ('win', 'loss')
            GROUP BY player_rank
            ORDER BY battle_count DESC, win_rate_pct DESC, player_rank ASC
            LIMIT 8
            """,
        )
    ]


def query_top_matchups(connection: sqlite3.Connection) -> list[tuple[str, str, int]]:
    return [
        (row["player_hero"], row["opponent_hero"], int(row["battle_count"]))
        for row in fetch_all(
            connection,
            """
            SELECT
              player_hero,
              opponent_hero,
              COUNT(*) AS battle_count
            FROM battle_replays
            WHERE player_hero IS NOT NULL
              AND opponent_hero IS NOT NULL
            GROUP BY player_hero, opponent_hero
            ORDER BY battle_count DESC, player_hero ASC, opponent_hero ASC
            LIMIT 10
            """,
        )
    ]


def query_hero_matchup_win_rates(connection: sqlite3.Connection) -> list[tuple[str, str, int, float]]:
    return [
        (
            row["player_hero"],
            row["opponent_hero"],
            int(row["battle_count"]),
            float(row["win_rate_pct"]),
        )
        for row in fetch_all(
            connection,
            """
            SELECT
              player_hero,
              opponent_hero,
              COUNT(*) AS battle_count,
              ROUND(
                100.0 * SUM(CASE WHEN result = 'win' THEN 1 ELSE 0 END) / COUNT(*),
                2
              ) AS win_rate_pct
            FROM battle_replays
            WHERE player_hero IS NOT NULL
              AND opponent_hero IS NOT NULL
              AND result IN ('win', 'loss')
            GROUP BY player_hero, opponent_hero
            ORDER BY win_rate_pct DESC, battle_count DESC, player_hero ASC, opponent_hero ASC
            LIMIT 12
            """,
        )
    ]


def query_rank_matchup_win_rates(connection: sqlite3.Connection) -> list[tuple[str, str, int, float]]:
    return [
        (
            row["player_rank"],
            row["opponent_rank"],
            int(row["battle_count"]),
            float(row["win_rate_pct"]),
        )
        for row in fetch_all(
            connection,
            """
            SELECT
              player_rank,
              opponent_rank,
              COUNT(*) AS battle_count,
              ROUND(
                100.0 * SUM(CASE WHEN result = 'win' THEN 1 ELSE 0 END) / COUNT(*),
                2
              ) AS win_rate_pct
            FROM battle_replays
            WHERE player_rank IS NOT NULL
              AND opponent_rank IS NOT NULL
              AND result IN ('win', 'loss')
            GROUP BY player_rank, opponent_rank
            ORDER BY battle_count DESC, win_rate_pct DESC, player_rank ASC, opponent_rank ASC
            LIMIT 12
            """,
        )
    ]


def query_replay_size_buckets(connection: sqlite3.Connection) -> list[tuple[str, int]]:
    sizes = [
        int(row["replay_size_bytes"])
        for row in fetch_all(
            connection,
            """
            SELECT replay_size_bytes
            FROM battle_replays
            WHERE replay_size_bytes IS NOT NULL
            ORDER BY replay_size_bytes
            """,
        )
    ]
    if not sizes:
        return []

    max_size = max(sizes)
    bucket_size = max(1024, int(math.ceil(max_size / 4 / 1024.0)) * 1024)
    bucket_count = max(1, math.ceil(max_size / bucket_size))
    buckets: list[tuple[str, int]] = []
    for index in range(bucket_count):
        lower = index * bucket_size
        upper = lower + bucket_size - 1
        label = f"{human_bytes(lower)}-{human_bytes(upper + 1)}"
        count = sum(1 for size in sizes if lower <= size <= upper)
        buckets.append((label, count))
    return buckets


def side_win_case(side: str) -> str:
    if side == "player":
        return "CASE WHEN result = 'win' THEN 1 ELSE 0 END"
    if side == "opponent":
        return "CASE WHEN result = 'loss' THEN 1 ELSE 0 END"
    raise ValueError(f"Unsupported side: {side}")


def query_top_cards(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit: int = 12,
) -> list[tuple[str, str, str, int, int, int, int, float]]:
    win_case = side_win_case(side)
    return [
        (
            row["card_name"],
            row["enchant"],
            row["tier"],
            int(row["days_seen"]),
            int(row["deduped_battles"]),
            int(row["occurrences"]),
            int(row["wins"]),
            float(row["win_rate_pct"] or 0.0),
        )
        for row in fetch_all(
            connection,
            f"""
            WITH card_battles AS (
              SELECT
                side,
                battle_day,
                card_name,
                enchant,
                tier,
                battle_card_key,
                MAX(CASE WHEN result IN ('win', 'loss') THEN 1 ELSE 0 END) AS has_decision,
                MAX({win_case}) AS win_flag,
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
              COALESCE(
                ROUND(
                  100.0 * SUM(CASE WHEN has_decision = 1 THEN win_flag ELSE 0 END)
                  / NULLIF(SUM(CASE WHEN has_decision = 1 THEN 1 ELSE 0 END), 0),
                  2
                ),
                0
              ) AS win_rate_pct
            FROM card_battles
            GROUP BY card_name, enchant, tier
            ORDER BY deduped_battles DESC, win_rate_pct DESC, card_name ASC, enchant ASC, tier ASC
            LIMIT ?
            """,
            (side, limit),
        )
    ]


def query_card_win_rates_by_day(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit_per_day: int = 8,
) -> list[tuple[int, str, str, str, int, int, int, float]]:
    win_case = side_win_case(side)
    return [
        (
            int(row["battle_day"]),
            row["card_name"],
            row["enchant"],
            row["tier"],
            int(row["deduped_battles"]),
            int(row["occurrences"]),
            int(row["wins"]),
            float(row["win_rate_pct"] or 0.0),
        )
        for row in fetch_all(
            connection,
            f"""
            WITH card_battles AS (
              SELECT
                battle_day,
                card_name,
                enchant,
                tier,
                battle_card_key,
                MAX(CASE WHEN result IN ('win', 'loss') THEN 1 ELSE 0 END) AS has_decision,
                MAX({win_case}) AS win_flag,
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
                COALESCE(
                  ROUND(
                    100.0 * SUM(CASE WHEN has_decision = 1 THEN win_flag ELSE 0 END)
                    / NULLIF(SUM(CASE WHEN has_decision = 1 THEN 1 ELSE 0 END), 0),
                    2
                  ),
                  0
                ) AS win_rate_pct
              FROM card_battles
              GROUP BY battle_day, card_name, enchant, tier
            ),
            ranked AS (
              SELECT
                battle_day,
                card_name,
                enchant,
                tier,
                deduped_battles,
                occurrences,
                wins,
                win_rate_pct,
                ROW_NUMBER() OVER (
                  PARTITION BY battle_day
                  ORDER BY deduped_battles DESC, win_rate_pct DESC, card_name ASC, enchant ASC, tier ASC
                ) AS row_num
              FROM aggregated
            )
            SELECT
              battle_day,
              card_name,
              enchant,
              tier,
              deduped_battles,
              occurrences,
              wins,
              win_rate_pct
            FROM ranked
            WHERE row_num <= ?
            ORDER BY battle_day ASC, deduped_battles DESC, win_rate_pct DESC, card_name ASC, enchant ASC, tier ASC
            """,
            (side, limit_per_day),
        )
    ]


def query_top_card_trends(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit: int = 5,
) -> list[tuple[int, str, int, int, float]]:
    win_case = side_win_case(side)
    return [
        (
            int(row["battle_day"]),
            row["card_label"],
            int(row["deduped_battles"]),
            int(row["wins"]),
            float(row["win_rate_pct"] or 0.0),
        )
        for row in fetch_all(
            connection,
            f"""
            WITH card_battles AS (
              SELECT
                side,
                battle_day,
                card_name,
                enchant,
                tier,
                battle_card_key,
                MAX(CASE WHEN result IN ('win', 'loss') THEN 1 ELSE 0 END) AS has_decision,
                MAX({win_case}) AS win_flag
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
            """,
            (side, limit),
        )
    ]


def query_day_card_heatmap(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit_per_day: int = 5,
) -> list[tuple[int, str, int, float]]:
    win_case = side_win_case(side)
    return [
        (
            int(row["battle_day"]),
            row["card_label"],
            int(row["deduped_battles"]),
            float(row["win_rate_pct"] or 0.0),
        )
        for row in fetch_all(
            connection,
            f"""
            WITH card_battles AS (
              SELECT
                battle_day,
                card_name,
                enchant,
                tier,
                battle_card_key,
                MAX(CASE WHEN result IN ('win', 'loss') THEN 1 ELSE 0 END) AS has_decision,
                MAX({win_case}) AS win_flag
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
            """,
            (side, limit_per_day),
        )
    ]


def query_day_leaders(
    connection: sqlite3.Connection,
    *,
    side: str,
    limit_per_day: int = 5,
) -> list[tuple[int, str, str, str, int, int, int, float]]:
    return query_card_win_rates_by_day(connection, side=side, limit_per_day=limit_per_day)


def query_card_projection_status(connection: sqlite3.Connection) -> tuple[int, int]:
    battle_count = int(connection.execute("SELECT COUNT(*) FROM battle_replays").fetchone()[0])
    card_count = int(connection.execute("SELECT COUNT(*) FROM battle_replay_cards").fetchone()[0])
    return battle_count, card_count


def human_bytes(value: int) -> str:
    if value < 1024:
        return f"{value} B"
    if value < 1024 * 1024:
        return f"{value / 1024:.1f} KB"
    return f"{value / (1024 * 1024):.1f} MB"


def render_bar_chart(
    title: str,
    data: Sequence[tuple[str, float | int]],
    *,
    value_formatter: str = "{value}",
    width: int = 720,
    height: int = 320,
    color: str = "#1f6feb",
) -> str:
    if not data:
        return render_empty_chart(title)

    left = 56
    right = 16
    top = 24
    bottom = 64
    inner_width = width - left - right
    inner_height = height - top - bottom
    max_value = max(float(value) for _, value in data) or 1.0
    bar_width = inner_width / max(len(data), 1)

    parts = [
        f'<svg viewBox="0 0 {width} {height}" class="chart" role="img" aria-label="{escape(title)}">'
    ]
    for tick in range(5):
        y = top + inner_height * tick / 4
        value = max_value * (4 - tick) / 4
        parts.append(
            f'<line x1="{left}" y1="{y:.1f}" x2="{width - right}" y2="{y:.1f}" class="grid" />'
        )
        parts.append(
            f'<text x="{left - 8}" y="{y + 4:.1f}" class="axis-label axis-value">{escape(value_formatter.format(value=round(value, 2)))}</text>'
        )

    for index, (label, raw_value) in enumerate(data):
        value = float(raw_value)
        bar_height = 0 if max_value == 0 else inner_height * value / max_value
        x = left + index * bar_width + 8
        y = top + inner_height - bar_height
        width_px = max(bar_width - 16, 12)
        parts.append(
            f'<rect x="{x:.1f}" y="{y:.1f}" width="{width_px:.1f}" height="{bar_height:.1f}" rx="6" fill="{color}" />'
        )
        parts.append(
            f'<text x="{x + width_px / 2:.1f}" y="{y - 8:.1f}" class="value-label">{escape(value_formatter.format(value=raw_value))}</text>'
        )
        parts.append(
            f'<text x="{x + width_px / 2:.1f}" y="{height - 18}" class="axis-label x-label">{escape(label)}</text>'
        )

    parts.append("</svg>")
    return "\n".join(parts)


def render_line_chart(
    title: str,
    data: Sequence[tuple[str, float | int]],
    *,
    value_formatter: str = "{value}",
    width: int = 720,
    height: int = 320,
    color: str = "#d97706",
) -> str:
    if not data:
        return render_empty_chart(title)

    left = 56
    right = 16
    top = 24
    bottom = 64
    inner_width = width - left - right
    inner_height = height - top - bottom
    max_value = max(float(value) for _, value in data) or 1.0
    step_x = inner_width / max(len(data) - 1, 1)
    points: list[str] = []

    parts = [
        f'<svg viewBox="0 0 {width} {height}" class="chart" role="img" aria-label="{escape(title)}">'
    ]
    for tick in range(5):
        y = top + inner_height * tick / 4
        value = max_value * (4 - tick) / 4
        parts.append(
            f'<line x1="{left}" y1="{y:.1f}" x2="{width - right}" y2="{y:.1f}" class="grid" />'
        )
        parts.append(
            f'<text x="{left - 8}" y="{y + 4:.1f}" class="axis-label axis-value">{escape(value_formatter.format(value=round(value, 2)))}</text>'
        )

    for index, (label, raw_value) in enumerate(data):
        value = float(raw_value)
        x = left + index * step_x
        y = top + inner_height - (0 if max_value == 0 else inner_height * value / max_value)
        points.append(f"{x:.1f},{y:.1f}")
        parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="5" fill="{color}" />')
        parts.append(
            f'<text x="{x:.1f}" y="{y - 10:.1f}" class="value-label">{escape(value_formatter.format(value=raw_value))}</text>'
        )
        parts.append(
            f'<text x="{x:.1f}" y="{height - 18}" class="axis-label x-label">{escape(label)}</text>'
        )

    parts.insert(
        1,
        f'<polyline fill="none" stroke="{color}" stroke-width="3" points="{" ".join(points)}" />',
    )
    parts.append("</svg>")
    return "\n".join(parts)


def render_multi_series_line_chart(
    title: str,
    x_labels: Sequence[str],
    series: Sequence[tuple[str, Sequence[float | int]]],
    *,
    value_formatter: str = "{value}",
    width: int = 720,
    height: int = 360,
) -> str:
    if not x_labels or not series:
        return render_empty_chart(title)

    left = 56
    right = 20
    top = 24
    bottom = 72
    legend_height = 20 + 20 * len(series)
    inner_width = width - left - right
    inner_height = height - top - bottom - legend_height
    max_value = max(float(value) for _, values in series for value in values) or 1.0
    step_x = inner_width / max(len(x_labels) - 1, 1)
    palette = ("#b45309", "#15803d", "#2563eb", "#dc2626", "#7c3aed", "#0f766e")

    parts = [
        f'<svg viewBox="0 0 {width} {height}" class="chart" role="img" aria-label="{escape(title)}">'
    ]
    for tick in range(5):
        y = top + inner_height * tick / 4
        value = max_value * (4 - tick) / 4
        parts.append(
            f'<line x1="{left}" y1="{y:.1f}" x2="{width - right}" y2="{y:.1f}" class="grid" />'
        )
        parts.append(
            f'<text x="{left - 8}" y="{y + 4:.1f}" class="axis-label axis-value">{escape(value_formatter.format(value=round(value, 2)))}</text>'
        )

    for index, label in enumerate(x_labels):
        x = left + index * step_x
        parts.append(
            f'<text x="{x:.1f}" y="{top + inner_height + 24:.1f}" class="axis-label x-label">{escape(label)}</text>'
        )

    for series_index, (series_label, values) in enumerate(series):
        color = palette[series_index % len(palette)]
        points: list[str] = []
        for value_index, raw_value in enumerate(values):
            value = float(raw_value)
            x = left + value_index * step_x
            y = top + inner_height - (0 if max_value == 0 else inner_height * value / max_value)
            points.append(f"{x:.1f},{y:.1f}")
            parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="4" fill="{color}" />')

        parts.append(
            f'<polyline fill="none" stroke="{color}" stroke-width="3" points="{" ".join(points)}" />'
        )

        legend_y = top + inner_height + 40 + 20 * series_index
        parts.append(
            f'<line x1="{left:.1f}" y1="{legend_y:.1f}" x2="{left + 18:.1f}" y2="{legend_y:.1f}" stroke="{color}" stroke-width="3" />'
        )
        parts.append(
            f'<text x="{left + 26:.1f}" y="{legend_y + 4:.1f}" class="axis-label">{escape(series_label)}</text>'
        )

    parts.append("</svg>")
    return "\n".join(parts)


def render_empty_chart(title: str) -> str:
    return (
        f'<div class="empty-chart" aria-label="{escape(title)}">'
        "No data available."
        "</div>"
    )


def render_kpis(kpis: dict[str, str]) -> str:
    cards = []
    for label, value in kpis.items():
        cards.append(
            "<div class=\"kpi-card\">"
            f"<div class=\"kpi-label\">{escape(label)}</div>"
            f"<div class=\"kpi-value\">{escape(value)}</div>"
            "</div>"
        )
    return "<section><h2>Overview</h2><div class=\"kpi-grid\">" + "".join(cards) + "</div></section>"


def render_table(title: str, headers: Sequence[str], rows: Iterable[Sequence[object]]) -> str:
    body_rows = []
    for row in rows:
        cells = "".join(f"<td>{escape(str(cell))}</td>" for cell in row)
        body_rows.append(f"<tr>{cells}</tr>")
    if not body_rows:
        body_rows.append(f"<tr><td colspan=\"{len(headers)}\">No data available.</td></tr>")
    header_html = "".join(f"<th>{escape(header)}</th>" for header in headers)
    return (
        "<section class=\"table-section\">"
        f"<h2>{escape(title)}</h2>"
        "<table>"
        f"<thead><tr>{header_html}</tr></thead>"
        f"<tbody>{''.join(body_rows)}</tbody>"
        "</table>"
        "</section>"
    )


def heatmap_cell_color(win_rate_pct: float) -> str:
    if win_rate_pct >= 75:
        return "#bfe3c0"
    if win_rate_pct >= 50:
        return "#efe7d8"
    if win_rate_pct > 0:
        return "#f1d8c8"
    return "#f6e6de"


def render_heatmap_section(
    title: str,
    description: str,
    rows: Sequence[str],
    columns: Sequence[str],
    values: dict[tuple[str, str], tuple[float, int]],
) -> str:
    if not rows or not columns:
        return (
            "<section>"
            f"<h2>{escape(title)}</h2>"
            f"<p>{escape(description)}</p>"
            f"{render_empty_chart(title)}"
            "</section>"
        )

    header_html = "".join(f"<th>{escape(column)}</th>" for column in columns)
    body_rows = []
    for row_label in rows:
        cells = [f"<th>{escape(row_label)}</th>"]
        for column in columns:
            value = values.get((row_label, column))
            if value is None:
                cells.append('<td class="heatmap-empty">-</td>')
                continue
            win_rate_pct, battles = value
            cells.append(
                f'<td class="heatmap-cell" style="background:{heatmap_cell_color(win_rate_pct)};">'
                f'<div class="heatmap-rate">{escape(f"{win_rate_pct:.2f}%")}</div>'
                f'<div class="heatmap-count">{escape(f"{battles} battles")}</div>'
                "</td>"
            )
        body_rows.append(f"<tr>{''.join(cells)}</tr>")

    return (
        "<section class=\"table-section\">"
        f"<h2>{escape(title)}</h2>"
        f"<p>{escape(description)}</p>"
        "<table class=\"heatmap-table\">"
        f"<thead><tr><th>Card</th>{header_html}</tr></thead>"
        f"<tbody>{''.join(body_rows)}</tbody>"
        "</table>"
        "</section>"
    )


def render_notice_section(title: str, body: str) -> str:
    return (
        '<section class="notice-section">'
        f"<h2>{escape(title)}</h2>"
        f"<p>{escape(body)}</p>"
        "</section>"
    )


def build_trend_chart(
    title: str,
    trend_rows: Sequence[tuple[int, str, int, int, float]],
) -> str:
    if not trend_rows:
        return render_empty_chart(title)

    days = sorted({battle_day for battle_day, _, _, _, _ in trend_rows})
    x_labels = [f"Day {day}" for day in days]
    labels = sorted({card_label for _, card_label, _, _, _ in trend_rows})
    trend_map = {
        (battle_day, card_label): win_rate_pct
        for battle_day, card_label, _, _, win_rate_pct in trend_rows
    }
    series = [
        (
            card_label,
            [trend_map.get((day, card_label), 0.0) for day in days],
        )
        for card_label in labels
    ]
    return render_multi_series_line_chart(
        title,
        x_labels,
        series,
        value_formatter="{value}%",
    )


def build_heatmap_inputs(
    heatmap_rows: Sequence[tuple[int, str, int, float]],
) -> tuple[list[str], list[str], dict[tuple[str, str], tuple[float, int]]]:
    if not heatmap_rows:
        return [], [], {}

    day_numbers = sorted({battle_day for battle_day, _, _, _ in heatmap_rows})
    columns = [f"Day {day}" for day in day_numbers]
    rows = sorted({card_label for _, card_label, _, _ in heatmap_rows})
    values = {
        (card_label, f"Day {battle_day}"): (win_rate_pct, deduped_battles)
        for battle_day, card_label, deduped_battles, win_rate_pct in heatmap_rows
    }
    return rows, columns, values


def build_report_html(connection: sqlite3.Connection) -> str:
    kpis = query_kpis(connection)
    daily = query_daily_battles(connection)
    hourly = query_hourly_battles(connection)
    hero_win_rates = query_hero_win_rates(connection)
    rank_win_rates = query_rank_win_rates(connection)
    matchups = query_top_matchups(connection)
    hero_matchup_win_rates = query_hero_matchup_win_rates(connection)
    rank_matchup_win_rates = query_rank_matchup_win_rates(connection)
    replay_buckets = query_replay_size_buckets(connection)
    top_player_cards = query_top_cards(connection, side="player")
    top_opponent_cards = query_top_cards(connection, side="opponent")
    player_cards_by_day = query_card_win_rates_by_day(connection, side="player")
    opponent_cards_by_day = query_card_win_rates_by_day(connection, side="opponent")
    player_trends = query_top_card_trends(connection, side="player")
    opponent_trends = query_top_card_trends(connection, side="opponent")
    player_heatmap = query_day_card_heatmap(connection, side="player")
    opponent_heatmap = query_day_card_heatmap(connection, side="opponent")
    player_day_leaders = query_day_leaders(connection, side="player")
    opponent_day_leaders = query_day_leaders(connection, side="opponent")

    player_trend_chart = build_trend_chart(
        "Top Player Cards Across Days",
        player_trends,
    )
    opponent_trend_chart = build_trend_chart(
        "Top Opponent Cards Across Days",
        opponent_trends,
    )
    battle_count, card_count = query_card_projection_status(connection)
    player_heatmap_rows, player_heatmap_columns, player_heatmap_values = build_heatmap_inputs(
        player_heatmap
    )
    opponent_heatmap_rows, opponent_heatmap_columns, opponent_heatmap_values = build_heatmap_inputs(
        opponent_heatmap
    )

    sections = [
        render_kpis(kpis),
        render_chart_section(
            "Daily Battle Trend",
            "Battle count by recorded UTC day.",
            render_line_chart("Daily Battle Trend", daily),
        ),
        render_chart_section(
            "Hourly Battle Distribution",
            "Battle count by recorded hour.",
            render_bar_chart("Hourly Battle Distribution", hourly),
        ),
    ]

    if battle_count > 0 and card_count == 0:
        sections.append(
            render_notice_section(
                "Card Analytics Metadata Is Empty",
                "Card analytics metadata is empty for the current clone database. Rebuild the local projection with rebuild_local_r2_metadata.py to backfill battle_replay_cards for existing mirrored battles.",
            )
        )

    sections.extend([
        render_chart_section(
            "Hero Win Rates",
            "Win rate by player hero for battles with known results.",
            render_bar_chart(
                "Hero Win Rates",
                [(hero, rate) for hero, _, rate in hero_win_rates],
                value_formatter="{value}%",
                color="#15803d",
            ),
        ),
        render_table(
            "Hero Win Rate Details",
            ("Hero", "Battles", "Win Rate %"),
            ((hero, count, f"{rate:.2f}") for hero, count, rate in hero_win_rates),
        ),
        render_chart_section(
            "Rank Win Rates",
            "Win rate by player rank for battles with known results.",
            render_bar_chart(
                "Rank Win Rates",
                [(rank, rate) for rank, _, rate in rank_win_rates],
                value_formatter="{value}%",
                color="#b45309",
            ),
        ),
        render_table(
            "Rank Win Rate Details",
            ("Rank", "Battles", "Win Rate %"),
            ((rank, count, f"{rate:.2f}") for rank, count, rate in rank_win_rates),
        ),
        render_chart_section(
            "Top Player Cards Across Days",
            "Win-rate trends for the most common player-side cards across battle days.",
            player_trend_chart,
        ),
        render_chart_section(
            "Top Opponent Cards Across Days",
            "Win-rate trends for the most common opponent-side cards across battle days.",
            opponent_trend_chart,
        ),
        render_heatmap_section(
            "Player Day-Card Heatmap",
            "Per-day top player-side cards with win-rate intensity and deduped battle counts.",
            player_heatmap_rows,
            player_heatmap_columns,
            player_heatmap_values,
        ),
        render_heatmap_section(
            "Opponent Day-Card Heatmap",
            "Per-day top opponent-side cards with win-rate intensity and deduped battle counts.",
            opponent_heatmap_rows,
            opponent_heatmap_columns,
            opponent_heatmap_values,
        ),
        render_table(
            "Top Player Cards",
            ("Card", "Enchant", "Tier", "Days Seen", "Battles", "Occurrences", "Wins", "Win Rate %"),
            (
                (name, enchant, tier, days_seen, battles, occurrences, wins, f"{rate:.2f}")
                for name, enchant, tier, days_seen, battles, occurrences, wins, rate in top_player_cards
            ),
        ),
        render_table(
            "Top Opponent Cards",
            ("Card", "Enchant", "Tier", "Days Seen", "Battles", "Occurrences", "Wins", "Win Rate %"),
            (
                (name, enchant, tier, days_seen, battles, occurrences, wins, f"{rate:.2f}")
                for name, enchant, tier, days_seen, battles, occurrences, wins, rate in top_opponent_cards
            ),
        ),
        render_table(
            "Player Card Win Rates By Day",
            ("Day", "Card", "Enchant", "Tier", "Battles", "Occurrences", "Wins", "Win Rate %"),
            (
                (day, name, enchant, tier, battles, occurrences, wins, f"{rate:.2f}")
                for day, name, enchant, tier, battles, occurrences, wins, rate in player_cards_by_day
            ),
        ),
        render_table(
            "Opponent Card Win Rates By Day",
            ("Day", "Card", "Enchant", "Tier", "Battles", "Occurrences", "Wins", "Win Rate %"),
            (
                (day, name, enchant, tier, battles, occurrences, wins, f"{rate:.2f}")
                for day, name, enchant, tier, battles, occurrences, wins, rate in opponent_cards_by_day
            ),
        ),
        render_table(
            "Player Day Leaders",
            ("Day", "Card", "Enchant", "Tier", "Battles", "Occurrences", "Wins", "Win Rate %"),
            (
                (day, name, enchant, tier, battles, occurrences, wins, f"{rate:.2f}")
                for day, name, enchant, tier, battles, occurrences, wins, rate in player_day_leaders
            ),
        ),
        render_table(
            "Opponent Day Leaders",
            ("Day", "Card", "Enchant", "Tier", "Battles", "Occurrences", "Wins", "Win Rate %"),
            (
                (day, name, enchant, tier, battles, occurrences, wins, f"{rate:.2f}")
                for day, name, enchant, tier, battles, occurrences, wins, rate in opponent_day_leaders
            ),
        ),
        render_table(
            "Top Hero Matchups",
            ("Player Hero", "Opponent Hero", "Battles"),
            matchups,
        ),
        render_table(
            "Hero Matchup Win Rates",
            ("Matchup", "Battles", "Win Rate %"),
            (
                (f"{player_hero} vs {opponent_hero}", battle_count, f"{win_rate:.2f}")
                for player_hero, opponent_hero, battle_count, win_rate in hero_matchup_win_rates
            ),
        ),
        render_table(
            "Rank Matchup Win Rates",
            ("Rank Matchup", "Battles", "Win Rate %"),
            (
                (f"{player_rank} vs {opponent_rank}", battle_count, f"{win_rate:.2f}")
                for player_rank, opponent_rank, battle_count, win_rate in rank_matchup_win_rates
            ),
        ),
        render_chart_section(
            "Replay Size Distribution",
            "Replay file size buckets projected from local clone metadata.",
            render_bar_chart(
                "Replay Size Distribution",
                replay_buckets,
                color="#7c3aed",
            ),
        ),
    ])

    return f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Local R2 Battles Report</title>
  <style>
    :root {{
      color-scheme: light;
      --bg: #f6efe5;
      --panel: #fffdf8;
      --panel-strong: #f4e8d5;
      --text: #1f2937;
      --muted: #6b7280;
      --border: #dbc8ab;
      --grid: #e5d8c3;
      --shadow: rgba(91, 62, 28, 0.08);
    }}

    * {{ box-sizing: border-box; }}
    body {{
      margin: 0;
      font-family: Georgia, "Iowan Old Style", "Palatino Linotype", serif;
      background:
        radial-gradient(circle at top left, #fff7eb 0, #fff7eb 14rem, transparent 14rem),
        linear-gradient(180deg, #f8f1e7 0%, var(--bg) 100%);
      color: var(--text);
    }}

    main {{
      max-width: 1200px;
      margin: 0 auto;
      padding: 32px 20px 48px;
    }}

    header {{
      margin-bottom: 28px;
      padding: 28px;
      background: var(--panel);
      border: 1px solid var(--border);
      border-radius: 20px;
      box-shadow: 0 14px 40px var(--shadow);
    }}

    h1, h2 {{
      margin: 0 0 12px;
      font-weight: 700;
      letter-spacing: 0.01em;
    }}

    p {{
      margin: 0;
      color: var(--muted);
      line-height: 1.5;
    }}

    section {{
      margin-top: 20px;
      padding: 22px;
      background: var(--panel);
      border: 1px solid var(--border);
      border-radius: 18px;
      box-shadow: 0 10px 30px var(--shadow);
    }}

    .kpi-grid {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
      gap: 14px;
    }}

    .kpi-card {{
      padding: 16px;
      background: linear-gradient(180deg, #fffefb 0%, var(--panel-strong) 100%);
      border-radius: 14px;
      border: 1px solid var(--border);
    }}

    .kpi-label {{
      font-size: 0.82rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--muted);
    }}

    .kpi-value {{
      margin-top: 10px;
      font-size: 1.65rem;
      font-weight: 700;
    }}

    .chart-wrap {{
      margin-top: 16px;
      overflow-x: auto;
    }}

    .chart {{
      width: 100%;
      min-width: 680px;
      height: auto;
      display: block;
    }}

    .grid {{
      stroke: var(--grid);
      stroke-width: 1;
    }}

    .axis-label {{
      fill: var(--muted);
      font-size: 11px;
      font-family: "Avenir Next", "Helvetica Neue", sans-serif;
    }}

    .axis-value {{
      text-anchor: end;
    }}

    .x-label {{
      text-anchor: middle;
    }}

    .value-label {{
      fill: var(--text);
      font-size: 11px;
      font-weight: 600;
      text-anchor: middle;
      font-family: "Avenir Next", "Helvetica Neue", sans-serif;
    }}

    .empty-chart {{
      margin-top: 16px;
      padding: 36px 20px;
      border: 1px dashed var(--border);
      border-radius: 14px;
      color: var(--muted);
      text-align: center;
      background: #fffaf2;
    }}

    table {{
      width: 100%;
      border-collapse: collapse;
      margin-top: 16px;
      font-family: "Avenir Next", "Helvetica Neue", sans-serif;
      font-size: 0.95rem;
    }}

    th, td {{
      padding: 10px 12px;
      border-bottom: 1px solid var(--grid);
      text-align: left;
    }}

    th {{
      color: var(--muted);
      font-size: 0.82rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }}

    .heatmap-table th:first-child {{
      min-width: 230px;
    }}

    .heatmap-cell {{
      min-width: 110px;
      text-align: center;
      vertical-align: middle;
    }}

    .heatmap-empty {{
      color: var(--muted);
      text-align: center;
      background: #fbf5ec;
    }}

    .heatmap-rate {{
      font-weight: 700;
      color: var(--text);
    }}

    .heatmap-count {{
      margin-top: 4px;
      font-size: 0.78rem;
      color: var(--muted);
    }}

    .notice-section {{
      background: linear-gradient(180deg, #fff9ee 0%, #f8edd9 100%);
      border-color: #d7b778;
    }}

    @media (max-width: 720px) {{
      main {{ padding: 20px 14px 32px; }}
      header, section {{ padding: 18px; }}
    }}
  </style>
</head>
<body>
  <main>
    <header>
      <h1>Local R2 Battles Report</h1>
      <p>Generated from <code>battle_replays</code> in the local clone SQLite database.</p>
    </header>
    {''.join(sections)}
  </main>
</body>
</html>
"""


def render_chart_section(title: str, description: str, chart_html: str) -> str:
    return (
        "<section>"
        f"<h2>{escape(title)}</h2>"
        f"<p>{escape(description)}</p>"
        f"<div class=\"chart-wrap\">{chart_html}</div>"
        "</section>"
    )


def escape(value: str) -> str:
    return html.escape(value, quote=True)


def render_battles_report(paths: ClonePaths, *, output_dir: Path | None = None) -> Path:
    target_dir = output_dir or (paths.runtime_dir / "reports")
    target_dir.mkdir(parents=True, exist_ok=True)
    report_path = target_dir / "index.html"

    with connect_db(paths.db_path) as connection:
        html_text = build_report_html(connection)

    report_path.write_text(html_text, encoding="utf-8")
    return report_path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Render a battles analysis dashboard from local_r2_clone SQLite metadata.",
    )
    parser.add_argument(
        "--base-dir",
        type=Path,
        default=DEFAULT_BASE_DIR,
        help="Base directory for local_r2_clone runtime data.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=None,
        help="Directory to write the generated report into. Defaults to <base-dir>/runtime/reports.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    report_path = render_battles_report(
      ClonePaths(args.base_dir.resolve()),
      output_dir=args.output_dir.resolve() if args.output_dir is not None else None,
    )
    print(report_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
