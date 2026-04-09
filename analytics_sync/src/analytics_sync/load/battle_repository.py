from __future__ import annotations

from datetime import UTC, datetime

from analytics_sync.load.provider import SqlExecutor


def _utc_now() -> str:
    return datetime.now(UTC).isoformat().replace("+00:00", "Z")


def build_replace_statements(battle_id: str) -> list[str]:
    return [
        f"DELETE FROM battle_cards WHERE battle_id = ? -- {battle_id}",
        f"DELETE FROM battle_skills WHERE battle_id = ? -- {battle_id}",
        f"DELETE FROM battle_slot_temperatures WHERE battle_id = ? -- {battle_id}",
    ]


class BattleRepository:
    def __init__(self, client: SqlExecutor) -> None:
        self._client = client

    def upsert_battle(
        self,
        *,
        battle_row: dict[str, object],
        card_rows: list[dict[str, object]],
        skill_rows: list[dict[str, object]],
        temperature_rows: list[dict[str, object]],
    ) -> None:
        battle_id = battle_row["battle_id"]
        now = _utc_now()
        if self._client.dialect == "mysql":
            upsert_sql = """
            INSERT INTO battles (
                battle_id,
                run_id,
                recorded_at_utc,
                day,
                player_name,
                player_account_id,
                player_hero,
                player_rank,
                player_rating,
                player_level,
                opponent_name,
                opponent_account_id,
                opponent_hero,
                opponent_rank,
                opponent_rating,
                opponent_level,
                result,
                created_at,
                updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON DUPLICATE KEY UPDATE
                run_id = VALUES(run_id),
                recorded_at_utc = VALUES(recorded_at_utc),
                day = VALUES(day),
                player_name = VALUES(player_name),
                player_account_id = VALUES(player_account_id),
                player_hero = VALUES(player_hero),
                player_rank = VALUES(player_rank),
                player_rating = VALUES(player_rating),
                player_level = VALUES(player_level),
                opponent_name = VALUES(opponent_name),
                opponent_account_id = VALUES(opponent_account_id),
                opponent_hero = VALUES(opponent_hero),
                opponent_rank = VALUES(opponent_rank),
                opponent_rating = VALUES(opponent_rating),
                opponent_level = VALUES(opponent_level),
                result = VALUES(result),
                updated_at = VALUES(updated_at)
            """
        else:
            upsert_sql = """
            INSERT INTO battles (
                battle_id,
                run_id,
                recorded_at_utc,
                day,
                player_name,
                player_account_id,
                player_hero,
                player_rank,
                player_rating,
                player_level,
                opponent_name,
                opponent_account_id,
                opponent_hero,
                opponent_rank,
                opponent_rating,
                opponent_level,
                result,
                created_at,
                updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(battle_id) DO UPDATE SET
                run_id = excluded.run_id,
                recorded_at_utc = excluded.recorded_at_utc,
                day = excluded.day,
                player_name = excluded.player_name,
                player_account_id = excluded.player_account_id,
                player_hero = excluded.player_hero,
                player_rank = excluded.player_rank,
                player_rating = excluded.player_rating,
                player_level = excluded.player_level,
                opponent_name = excluded.opponent_name,
                opponent_account_id = excluded.opponent_account_id,
                opponent_hero = excluded.opponent_hero,
                opponent_rank = excluded.opponent_rank,
                opponent_rating = excluded.opponent_rating,
                opponent_level = excluded.opponent_level,
                result = excluded.result,
                updated_at = excluded.updated_at
            """
        self._client.execute(
            upsert_sql,
            [
                battle_row["battle_id"],
                battle_row["run_id"],
                battle_row["recorded_at_utc"],
                battle_row.get("day"),
                battle_row.get("player_name"),
                battle_row.get("player_account_id"),
                battle_row.get("player_hero"),
                battle_row.get("player_rank"),
                battle_row.get("player_rating"),
                battle_row.get("player_level"),
                battle_row.get("opponent_name"),
                battle_row.get("opponent_account_id"),
                battle_row.get("opponent_hero"),
                battle_row.get("opponent_rank"),
                battle_row.get("opponent_rating"),
                battle_row.get("opponent_level"),
                battle_row.get("result"),
                now,
                now,
            ],
        )

        for table_name in ("battle_cards", "battle_skills", "battle_slot_temperatures"):
            self._client.execute(f"DELETE FROM {table_name} WHERE battle_id = ?", [battle_id])

        self._client.executemany(
            """
            INSERT INTO battle_cards (
                battle_id, side, slot_index, card_template_id, card_tier, enchant_code, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            [
                [
                    row["battle_id"],
                    row["side"],
                    row["slot_index"],
                    row["card_template_id"],
                    row.get("card_tier"),
                    row.get("enchant_code"),
                    now,
                ]
                for row in card_rows
            ],
        )

        self._client.executemany(
            """
            INSERT INTO battle_skills (
                battle_id, side, slot_index, skill_template_id, skill_tier, created_at
            ) VALUES (?, ?, ?, ?, ?, ?)
            """,
            [
                [
                    row["battle_id"],
                    row["side"],
                    row["slot_index"],
                    row["skill_template_id"],
                    row.get("skill_tier"),
                    now,
                ]
                for row in skill_rows
            ],
        )

        self._client.executemany(
            """
            INSERT INTO battle_slot_temperatures (
                battle_id, side, slot_index, temperature_state, created_at
            ) VALUES (?, ?, ?, ?, ?)
            """,
            [
                [
                    row["battle_id"],
                    row["side"],
                    row["slot_index"],
                    row["temperature_state"],
                    now,
                ]
                for row in temperature_rows
            ],
        )
