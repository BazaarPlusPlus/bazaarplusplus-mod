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
                    "snapshots": {
                        "player_hand": {
                            "items": [
                                {
                                    "name": "Fiery Cutlass",
                                    "enchant": "Burning",
                                    "tier": "Silver",
                                },
                                {
                                    "name": "Fiery Cutlass",
                                    "tier": "Silver",
                                },
                            ]
                        },
                        "player_skills": {
                            "items": [
                                {"name": "Quick Thinking", "tier": "Gold"},
                            ]
                        },
                        "opponent_hand": {
                            "items": [
                                {
                                    "name": "Shield Wall",
                                    "enchant": "Heavy",
                                    "tier": "Bronze",
                                }
                            ]
                        },
                        "opponent_skills": {
                            "items": [
                                {"name": "Emergency Repairs", "tier": "Silver"},
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
                    "snapshots": {
                        "player_hand": {
                            "items": [
                                {
                                    "name": "Fiery Cutlass",
                                    "enchant": "Burning",
                                    "tier": "Silver",
                                }
                            ]
                        },
                        "player_skills": {
                            "items": [
                                {"name": "Quick Thinking", "tier": "Gold"},
                            ]
                        },
                        "opponent_hand": {
                            "items": [
                                {
                                    "name": "Shield Wall",
                                    "enchant": "Heavy",
                                    "tier": "Bronze",
                                },
                                {
                                    "name": "Shield Wall",
                                    "enchant": "Heavy",
                                    "tier": "Bronze",
                                },
                            ]
                        },
                        "opponent_skills": {
                            "items": [
                                {"name": "Emergency Repairs", "tier": "Silver"},
                            ]
                        },
                    },
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
                    "snapshots": {
                        "player_hand": {
                            "items": [
                                {"name": "Chrono Shield", "tier": "Gold"},
                            ]
                        },
                        "player_skills": {
                            "items": [
                                {"name": "Time Dilation", "tier": "Silver"},
                            ]
                        },
                        "opponent_hand": {
                            "items": [
                                {"name": "Steam Lance", "tier": "Bronze"},
                            ]
                        },
                        "opponent_skills": {
                            "items": [
                                {"name": "Backup Battery", "tier": "Silver"},
                            ]
                        },
                    },
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
                                {
                                    "name": "Fiery Cutlass",
                                    "enchant": "Burning",
                                    "tier": "Silver",
                                },
                                {"name": "Storm Lantern", "tier": "Gold"},
                            ]
                        },
                        "player_skills": {
                            "items": [
                                {"name": "Quick Thinking", "tier": "Gold"},
                            ]
                        },
                        "opponent_hand": {
                            "items": [
                                {
                                    "name": "Shield Wall",
                                    "enchant": "Heavy",
                                    "tier": "Bronze",
                                },
                                {"name": "Steam Lance", "tier": "Bronze"},
                            ]
                        },
                        "opponent_skills": {
                            "items": [
                                {"name": "Emergency Repairs", "tier": "Silver"},
                            ]
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
        self.assertIn("Top Player Cards", html)
        self.assertIn("Top Opponent Cards", html)
        self.assertIn("Player Card Win Rates By Day", html)
        self.assertIn("Opponent Card Win Rates By Day", html)
        self.assertIn("2026-04-01", html)
        self.assertIn("Vanessa", html)
        self.assertIn("Dooley", html)
        self.assertIn("Fiery Cutlass", html)
        self.assertIn("Shield Wall", html)
        self.assertIn("Burning", html)
        self.assertIn("Silver", html)

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

    def test_render_battles_report_includes_day_based_card_analytics(self) -> None:
        self.seed_projection()

        report_path = render_battles_report(self.paths)
        html = report_path.read_text(encoding="utf-8")

        self.assertIn("Player Card Win Rates By Day", html)
        self.assertIn("Opponent Card Win Rates By Day", html)
        self.assertIn("Fiery Cutlass", html)
        self.assertIn("Shield Wall", html)
        self.assertIn(">4<", html)
        self.assertIn("50.00", html)

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

    def test_render_battles_report_warns_when_card_projection_is_missing(self) -> None:
        self.seed_projection()

        self.assertTrue(self.paths.db_path.exists())

        import sqlite3

        connection = sqlite3.connect(self.paths.db_path)
        try:
            connection.execute("DELETE FROM battle_replay_cards")
            connection.commit()
        finally:
            connection.close()

        report_path = render_battles_report(self.paths)
        html = report_path.read_text(encoding="utf-8")

        self.assertIn("Card analytics metadata is empty", html)
        self.assertIn("rebuild_local_r2_metadata.py", html)

    def test_render_battles_report_sorts_hero_matchups_by_metric_before_volume(self) -> None:
        self.seed_projection()

        report_path = render_battles_report(self.paths)
        html = report_path.read_text(encoding="utf-8")

        self.assertLess(
            html.index("Dooley vs Vanessa"),
            html.index("Vanessa vs Dooley"),
        )

    def test_render_battles_report_includes_hero_ten_win_rate_sections(self) -> None:
        self.seed_projection()

        report_path = render_battles_report(self.paths)
        html = report_path.read_text(encoding="utf-8")

        self.assertIn("Hero 10-Win Rates", html)
        self.assertIn("Hero 10-Win Rate Details", html)
        self.assertIn("Vanessa", html)
        self.assertIn("Dooley", html)
        self.assertIn("100.00", html)

    def test_render_battles_report_uses_completed_runs_for_hero_ten_win_rates(self) -> None:
        self.seed_projection()

        report_path = render_battles_report(self.paths)
        html = report_path.read_text(encoding="utf-8")

        self.assertIn("Hero 10-Win Rate Details", html)
        self.assertIn("<td>Vanessa</td><td>1</td><td>1</td><td>100.00</td>", html)
        self.assertIn("<td>Dooley</td><td>1</td><td>0</td><td>0.00</td>", html)


if __name__ == "__main__":
    unittest.main()
