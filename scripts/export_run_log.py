#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sqlite3
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export BazaarPlusPlus run logs from SQLite.")
    parser.add_argument("--db", required=True, help="Path to the SQLite database.")
    parser.add_argument("--out", required=True, help="Directory to write JSON debug output.")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--run-id", help="Export a single run id.")
    group.add_argument("--all", action="store_true", help="Export all runs.")
    return parser.parse_args()


def row_to_dict(row: sqlite3.Row, keys: list[str]) -> dict[str, object]:
    return {key: row[key] for key in keys if row[key] is not None}


def write_json(path: Path, payload: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=True, separators=(",", ":")) + "\n")


def export_run(connection: sqlite3.Connection, out_root: Path, run_row: sqlite3.Row) -> None:
    run_id = run_row["run_id"]
    started_at_utc = run_row["started_at_utc"]
    date_partition = started_at_utc[:10]
    run_dir = out_root / date_partition / run_id
    run_dir.mkdir(parents=True, exist_ok=True)

    meta = row_to_dict(
        run_row,
        [
            "schema_version",
            "run_id",
            "started_at_utc",
            "hero",
            "game_mode",
            "day",
            "hour",
            "seed",
            "status",
        ],
    )
    write_json(run_dir / "meta.json", meta)

    event_rows = connection.execute(
        """
        SELECT payload_json
        FROM run_events
        WHERE run_id = ?
        ORDER BY seq ASC
        """,
        (run_id,),
    ).fetchall()
    with (run_dir / "events.ndjson").open("w", encoding="utf-8") as handle:
        for event_row in event_rows:
            handle.write(event_row["payload_json"])
            handle.write("\n")

    checkpoint_row = connection.execute(
        """
        SELECT
            schema_version,
            run_id,
            last_seq,
            last_seen_at_utc,
            day,
            hour,
            state,
            current_encounter_id,
            last_state_fingerprint,
            last_selection_fingerprint,
            pending_selection_seq,
            completed
        FROM run_checkpoints
        WHERE run_id = ?
        """,
        (run_id,),
    ).fetchone()
    if checkpoint_row is not None:
        checkpoint = row_to_dict(
            checkpoint_row,
            [
                "schema_version",
                "run_id",
                "last_seq",
                "last_seen_at_utc",
                "day",
                "hour",
                "state",
                "current_encounter_id",
                "last_state_fingerprint",
                "last_selection_fingerprint",
                "pending_selection_seq",
                "completed",
            ],
        )
        if "completed" in checkpoint:
            checkpoint["completed"] = bool(checkpoint["completed"])
        write_json(run_dir / "checkpoint.json", checkpoint)

    status_row = connection.execute(
        """
        SELECT
            schema_version,
            run_id,
            status,
            ended_at_utc,
            final_day,
            final_hour,
            victories,
            losses,
            reason
        FROM run_status
        WHERE run_id = ?
        """,
        (run_id,),
    ).fetchone()
    if status_row is not None:
        status = row_to_dict(
            status_row,
            [
                "schema_version",
                "run_id",
                "status",
                "ended_at_utc",
                "final_day",
                "final_hour",
                "victories",
                "losses",
                "reason",
            ],
        )
        write_json(run_dir / "status.json", status)


def main() -> int:
    args = parse_args()

    db_path = Path(args.db)
    out_root = Path(args.out)

    connection = sqlite3.connect(str(db_path))
    connection.row_factory = sqlite3.Row
    try:
        if args.run_id:
            runs = connection.execute(
                """
                SELECT
                    schema_version,
                    run_id,
                    started_at_utc,
                    hero,
                    game_mode,
                    day,
                    hour,
                    seed,
                    status
                FROM runs
                WHERE run_id = ?
                ORDER BY started_at_utc ASC
                """,
                (args.run_id,),
            ).fetchall()
        else:
            runs = connection.execute(
                """
                SELECT
                    schema_version,
                    run_id,
                    started_at_utc,
                    hero,
                    game_mode,
                    day,
                    hour,
                    seed,
                    status
                FROM runs
                ORDER BY started_at_utc ASC
                """
            ).fetchall()

        if not runs:
            raise SystemExit("No matching runs found.")

        for run_row in runs:
            export_run(connection, out_root, run_row)
    finally:
        connection.close()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
