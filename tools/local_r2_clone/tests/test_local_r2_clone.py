from __future__ import annotations

import json
import sqlite3
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tools.local_r2_clone.clone_tool import (
    ClonePaths,
    ensure_workspace,
    rebuild_metadata,
    run_sync,
)


def write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload), encoding="utf-8")


class LocalR2CloneTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.base_dir = Path(self.temp_dir.name)
        self.paths = ClonePaths(self.base_dir)
        ensure_workspace(self.paths)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def open_db(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.paths.db_path)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON;")
        return connection

    def test_ensure_workspace_creates_schema_and_runtime_layout(self) -> None:
        self.assertTrue(self.paths.mirror_dir.is_dir())
        self.assertTrue(self.paths.meta_dir.is_dir())
        self.assertTrue(self.paths.db_path.is_file())
        self.assertTrue(self.paths.sync_state_path.is_file())

        with self.open_db() as connection:
            objects = {
                row["name"]: row["type"]
                for row in connection.execute(
                    """
                    SELECT name, type
                    FROM sqlite_master
                    WHERE name IN (
                      'sync_runs',
                      'sync_run_errors',
                      'r2_objects',
                      'battle_replays',
                      'run_summaries',
                      'run_summaries_latest'
                    )
                    """
                )
            }

        self.assertEqual(objects["sync_runs"], "table")
        self.assertEqual(objects["sync_run_errors"], "table")
        self.assertEqual(objects["r2_objects"], "table")
        self.assertEqual(objects["battle_replays"], "table")
        self.assertEqual(objects["run_summaries"], "table")
        self.assertEqual(objects["run_summaries_latest"], "view")

    def test_rebuild_projects_history_and_latest_run_summary(self) -> None:
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
                        "player_hero": "Vanessa",
                        "player_rank": "Bronze",
                        "player_rating": 1000,
                        "player_level": 3,
                        "opponent_name": "Beta",
                        "opponent_account_id": "player-b",
                        "opponent_hero": "Dooley",
                        "opponent_rank": "Silver",
                        "opponent_rating": 1010,
                        "opponent_level": 4,
                    },
                    "outcome": {
                        "result": "win",
                        "winner_combatant_id": "Player",
                        "loser_combatant_id": "Opponent",
                    },
                    "snapshots": {"start": {}, "end": {}},
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
        write_json(
            self.paths.mirror_dir / "run-summaries/client-1/run-1/hash-old.json",
            {
                "run_id": "run-1",
                "status": "active",
                "hero_id": "hero-1",
                "hero_name": "Vanessa",
                "started_at_utc": "2026-04-01T00:00:00.000Z",
                "ended_at_utc": "2026-04-01T00:10:00.000Z",
                "final_day": 3,
                "final_wins": 2,
                "final_losses": 1,
                "mmr": 1001,
                "schema_version": 1,
            },
        )
        write_json(
            self.paths.mirror_dir / "run-summaries/client-1/run-1/hash-new.json",
            {
                "run_id": "run-1",
                "status": "completed",
                "hero_id": "hero-1",
                "hero_name": "Vanessa",
                "started_at_utc": "2026-04-01T00:00:00.000Z",
                "ended_at_utc": "2026-04-01T00:20:00.000Z",
                "final_day": 5,
                "final_wins": 4,
                "final_losses": 1,
                "mmr": 1200,
                "schema_version": 2,
            },
        )

        rebuild_metadata(self.paths)

        with self.open_db() as connection:
            battle_count = connection.execute(
                "SELECT COUNT(*) FROM battle_replays"
            ).fetchone()[0]
            summary_count = connection.execute(
                "SELECT COUNT(*) FROM run_summaries"
            ).fetchone()[0]
            latest_row = connection.execute(
                """
                SELECT run_id, status, ended_at_utc, final_day, mmr
                FROM run_summaries_latest
                WHERE run_id = 'run-1'
                """
            ).fetchone()

        self.assertEqual(battle_count, 1)
        self.assertEqual(summary_count, 2)
        self.assertIsNotNone(latest_row)
        self.assertEqual(latest_row["status"], "completed")
        self.assertEqual(latest_row["ended_at_utc"], "2026-04-01T00:20:00.000Z")
        self.assertEqual(latest_row["final_day"], 5)
        self.assertEqual(latest_row["mmr"], 1200)

    def test_sync_removes_objects_not_seen_in_current_mirror(self) -> None:
        first_summary = self.paths.mirror_dir / "run-summaries/client-1/run-1/hash-old.json"
        write_json(
            first_summary,
            {
                "run_id": "run-1",
                "status": "completed",
                "hero_id": "hero-1",
                "hero_name": "Vanessa",
                "started_at_utc": "2026-04-01T00:00:00.000Z",
                "ended_at_utc": "2026-04-01T00:20:00.000Z",
                "final_day": 5,
                "final_wins": 4,
                "final_losses": 1,
                "mmr": 1200,
                "schema_version": 2,
            },
        )

        rebuild_metadata(self.paths)
        first_summary.unlink()

        run_sync(
            self.paths,
            remote="ignored:bucket",
            rclone_runner=lambda args, **kwargs: mock.Mock(returncode=0),
        )

        with self.open_db() as connection:
            object_count = connection.execute(
                "SELECT COUNT(*) FROM r2_objects"
            ).fetchone()[0]
            summary_count = connection.execute(
                "SELECT COUNT(*) FROM run_summaries"
            ).fetchone()[0]
            latest_count = connection.execute(
                "SELECT COUNT(*) FROM run_summaries_latest"
            ).fetchone()[0]

        self.assertEqual(object_count, 0)
        self.assertEqual(summary_count, 0)
        self.assertEqual(latest_count, 0)

    def test_sync_updates_state_and_invokes_rclone(self) -> None:
        calls: list[list[str]] = []

        def fake_runner(args: list[str], **_: object) -> mock.Mock:
            calls.append(args)
            return mock.Mock(returncode=0)

        run_sync(self.paths, remote="remote-name:bucket", rclone_runner=fake_runner)

        self.assertEqual(
            calls,
            [["rclone", "sync", "remote-name:bucket", str(self.paths.mirror_dir)]],
        )

        state = json.loads(self.paths.sync_state_path.read_text(encoding="utf-8"))
        self.assertEqual(state["last_sync_status"], "succeeded")
        self.assertTrue(state["last_rclone_success"])
        self.assertTrue(state["last_sync_run_id"])


if __name__ == "__main__":
    unittest.main()
