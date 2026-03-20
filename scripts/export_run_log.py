#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sqlite3
from typing import Any
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


def write_ndjson(path: Path, payloads: list[dict[str, object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        for payload in payloads:
            handle.write(json.dumps(payload, ensure_ascii=True, separators=(",", ":")))
            handle.write("\n")


def load_event_payloads(event_rows: list[sqlite3.Row]) -> list[dict[str, Any]]:
    payloads: list[dict[str, Any]] = []
    for event_row in event_rows:
        payload = json.loads(event_row["payload_json"])
        if isinstance(payload, dict):
            payloads.append(payload)
    return payloads


def build_decision_chain(events: list[dict[str, Any]]) -> list[dict[str, Any]]:
    decisions: list[dict[str, Any]] = []
    selection_by_seq: dict[int, dict[str, Any]] = {}

    for event in events:
        seq = event.get("seq")
        if not isinstance(seq, int):
            continue

        kind = event.get("kind")
        if kind in {
            "selection_seen",
            "encounter_options_seen",
            "choice_options_seen",
            "loot_options_seen",
            "pedestal_options_seen",
        }:
            selection_by_seq[seq] = event
            continue

        if kind not in {
            "choice_made",
            "encounter_selected",
            "choice_selected",
            "loot_selected",
            "pedestal_selected",
            "selection_abandoned",
        }:
            continue

        selection_seq = event.get("selection_seq")
        if not isinstance(selection_seq, int):
            continue

        selection_event = selection_by_seq.get(selection_seq)
        decisions.append(
            {
                "selection_seq": selection_seq,
                "selection_kind": selection_event.get("kind") if selection_event else None,
                "selection_ts_utc": selection_event.get("ts") if selection_event else None,
                "selection_state": selection_event.get("state") if selection_event else None,
                "selection_day": selection_event.get("day") if selection_event else None,
                "selection_hour": selection_event.get("hour") if selection_event else None,
                "selection_encounter_id": (
                    selection_event.get("encounter_id") if selection_event else None
                ),
                "selection_parent_encounter_id": (
                    selection_event.get("parent_encounter_id") if selection_event else None
                ),
                "options": selection_event.get("options") if selection_event else None,
                "choice": {
                    "seq": seq,
                    "kind": kind,
                    "ts_utc": event.get("ts"),
                    "state": event.get("state"),
                    "encounter_id": event.get("encounter_id"),
                    "parent_encounter_id": event.get("parent_encounter_id"),
                    "selected_instance_id": event.get("selected_instance_id"),
                    "selected_template_id": event.get("selected_template_id"),
                    "selected_encounter_id": event.get("selected_encounter_id"),
                    "selected_name": event.get("selected_name"),
                    "selected_tier": event.get("selected_tier"),
                    "selected_enchant": event.get("selected_enchant"),
                    "abandoned_reason": event.get("abandoned_reason"),
                    "inferred_from": event.get("inferred_from"),
                    "confidence": event.get("confidence"),
                },
            }
        )

    return decisions


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
    event_payloads = load_event_payloads(event_rows)
    with (run_dir / "events.ndjson").open("w", encoding="utf-8") as handle:
        for event_row in event_rows:
            handle.write(event_row["payload_json"])
            handle.write("\n")

    decision_chain = build_decision_chain(event_payloads)
    if decision_chain:
        write_ndjson(run_dir / "decision_chain.ndjson", decision_chain)

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
            pending_selection_json,
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
                "pending_selection_json",
                "completed",
            ],
        )
        if "pending_selection_json" in checkpoint:
            checkpoint["pending_selection"] = json.loads(checkpoint.pop("pending_selection_json"))
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

    pvp_battle_rows = connection.execute(
        """
        SELECT
            battle_id,
            run_id,
            recorded_at_utc,
            day,
            hour,
            encounter_id,
            player_name,
            player_account_id,
            opponent_name,
            opponent_hero,
            opponent_rank,
            opponent_rating,
            opponent_level,
            opponent_account_id,
            combat_kind,
            result,
            winner_combatant_id,
            loser_combatant_id,
            player_hand_json,
            player_skills_json,
            opponent_hand_json,
            opponent_skills_json
        FROM pvp_battles
        WHERE run_id = ?
        ORDER BY recorded_at_utc ASC, battle_id ASC
        """,
        (run_id,),
    ).fetchall()
    if pvp_battle_rows:
        battles: list[dict[str, object]] = []
        for battle_row in pvp_battle_rows:
            battle: dict[str, object] = {
                "battle_id": battle_row["battle_id"],
                "recorded_at_utc": battle_row["recorded_at_utc"],
                "player_name": battle_row["player_name"],
                "player_account_id": battle_row["player_account_id"],
                "opponent_name": battle_row["opponent_name"],
                "opponent_hero": battle_row["opponent_hero"],
                "opponent_rank": battle_row["opponent_rank"],
                "opponent_rating": battle_row["opponent_rating"],
                "opponent_level": battle_row["opponent_level"],
                "opponent_account_id": battle_row["opponent_account_id"],
                "combat_kind": battle_row["combat_kind"],
                "result": battle_row["result"],
                "winner_combatant_id": battle_row["winner_combatant_id"],
                "loser_combatant_id": battle_row["loser_combatant_id"],
            }
            if battle_row["run_id"] is not None:
                battle["run_id"] = battle_row["run_id"]
            if battle_row["day"] is not None:
                battle["day"] = battle_row["day"]
            if battle_row["hour"] is not None:
                battle["hour"] = battle_row["hour"]
            if battle_row["encounter_id"] is not None:
                battle["encounter_id"] = battle_row["encounter_id"]
            battle["player_hand"] = json.loads(battle_row["player_hand_json"])
            battle["player_skills"] = json.loads(battle_row["player_skills_json"])
            battle["opponent_hand"] = json.loads(battle_row["opponent_hand_json"])
            battle["opponent_skills"] = json.loads(battle_row["opponent_skills_json"])
            battles.append(battle)

        write_ndjson(run_dir / "pvp_battles.ndjson", battles)


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
