from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from tools.local_r2_clone.clone_tool import ClonePaths, ensure_workspace, rebuild_metadata
from tools.local_r2_clone.render_battles_charts import render_battles_report


def write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload), encoding="utf-8")


class RenderBattlesReportTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.base_dir = Path(self.temp_dir.name)
        self.paths = ClonePaths(self.base_dir)
        ensure_workspace(self.paths)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def seed_projection(self) -> None:
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
            self.paths.mirror_dir / "battle-replays/client-1/battle-2/hash-b.json",
            {
                "battle_id": "battle-2",
                "run_id": "run-1",
                "schema_version": 3,
                "battle_manifest": {
                    "battle_id": "battle-2",
                    "run_id": "run-1",
                    "recorded_at_utc": "2026-04-01T05:00:00.000Z",
                    "day": 4,
                    "hour": 5,
                    "combat_kind": "PVPCombat",
                    "participants": {
                        "player_name": "Alpha",
                        "player_account_id": "player-a",
                        "player_hero": "Vanessa",
                        "player_rank": "Bronze",
                        "player_rating": 1005,
                        "player_level": 4,
                        "opponent_name": "Gamma",
                        "opponent_account_id": "player-c",
                        "opponent_hero": "Pygmalien",
                        "opponent_rank": "Gold",
                        "opponent_rating": 1035,
                        "opponent_level": 5,
                    },
                    "outcome": {
                        "result": "loss",
                        "winner_combatant_id": "Opponent",
                        "loser_combatant_id": "Player",
                    },
                    "snapshots": {"start": {}, "end": {}},
                },
                "replay_payload": {
                    "battle_id": "battle-2",
                    "version": 7,
                    "spawn_message_base64": "aa",
                    "combat_message_base64": "bb",
                    "despawn_message_base64": "cc",
                },
            },
        )
        write_json(
            self.paths.mirror_dir / "battle-replays/client-2/battle-3/hash-c.json",
            {
                "battle_id": "battle-3",
                "run_id": "run-2",
                "schema_version": 3,
                "battle_manifest": {
                    "battle_id": "battle-3",
                    "run_id": "run-2",
                    "recorded_at_utc": "2026-04-02T05:30:00.000Z",
                    "day": 7,
                    "hour": 5,
                    "combat_kind": "PVPCombat",
                    "participants": {
                        "player_name": "Delta",
                        "player_account_id": "player-d",
                        "player_hero": "Dooley",
                        "player_rank": "Silver",
                        "player_rating": 1110,
                        "player_level": 6,
                        "opponent_name": "Alpha",
                        "opponent_account_id": "player-a",
                        "opponent_hero": "Vanessa",
                        "opponent_rank": "Bronze",
                        "opponent_rating": 1012,
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
                    "battle_id": "battle-3",
                    "version": 7,
                    "spawn_message_base64": "aaa",
                    "combat_message_base64": "bbb",
                    "despawn_message_base64": "ccc",
                },
            },
        )

        rebuild_metadata(self.paths)

    def test_render_battles_report_writes_html_dashboard(self) -> None:
        self.seed_projection()

        report_path = render_battles_report(self.paths)

        self.assertEqual(report_path, self.paths.runtime_dir / "reports" / "index.html")
        self.assertTrue(report_path.is_file())

        html = report_path.read_text(encoding="utf-8")
        self.assertIn("Local R2 Battles Report", html)
        self.assertIn("Daily Battle Trend", html)
        self.assertIn("Hourly Battle Distribution", html)
        self.assertIn("Hero Win Rates", html)
        self.assertIn("Rank Win Rates", html)
        self.assertIn("Top Hero Matchups", html)
        self.assertIn("Hero Matchup Win Rates", html)
        self.assertIn("Replay Size Distribution", html)
        self.assertIn("2026-04-01", html)
        self.assertIn("Vanessa", html)
        self.assertIn("Dooley", html)

    def test_render_battles_report_includes_summary_kpis(self) -> None:
        self.seed_projection()

        report_path = render_battles_report(self.paths)
        html = report_path.read_text(encoding="utf-8")

        self.assertIn("Total Battles", html)
        self.assertIn(">3<", html)
        self.assertIn("Unique Players", html)
        self.assertIn(">2<", html)
        self.assertIn("Unique Opponents", html)
        self.assertIn(">3<", html)

    def test_render_battles_report_includes_rank_and_matchup_win_rate_details(self) -> None:
        self.seed_projection()

        report_path = render_battles_report(self.paths)
        html = report_path.read_text(encoding="utf-8")

        self.assertIn("Rank Win Rate Details", html)
        self.assertIn("Bronze", html)
        self.assertIn("50.00", html)
        self.assertIn("Hero Matchup Win Rates", html)
        self.assertIn("Vanessa vs Dooley", html)


if __name__ == "__main__":
    unittest.main()
