from __future__ import annotations

from analytics_sync.load.sqlite_client import SqliteClient


def build_replace_statements(battle_id: str) -> list[str]:
    return [
        f"DELETE FROM battle_cards WHERE battle_id = '{battle_id}'",
        f"DELETE FROM battle_skills WHERE battle_id = '{battle_id}'",
        f"DELETE FROM battle_slot_temperatures WHERE battle_id = '{battle_id}'",
    ]


class BattleRepository:
    def __init__(self, client: SqliteClient) -> None:
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
        self._client.execute(
            """
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
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'), datetime('now'))
            ON CONFLICT(battle_id) DO UPDATE SET
                run_id = excluded.run_id,
                recorded_at_utc = excluded.recorded_at_utc,
                result = excluded.result,
                updated_at = datetime('now')
            """,
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
            ],
        )

        for statement in build_replace_statements(str(battle_id)):
            self._client.execute(statement)

        for row in card_rows:
            self._client.execute(
                """
                INSERT INTO battle_cards (
                    battle_id, side, slot_index, card_template_id, card_tier, enchant_code, created_at
                ) VALUES (?, ?, ?, ?, ?, ?, datetime('now'))
                """,
                [
                    row["battle_id"],
                    row["side"],
                    row["slot_index"],
                    row["card_template_id"],
                    row.get("card_tier"),
                    row.get("enchant_code"),
                ],
            )

        for row in skill_rows:
            self._client.execute(
                """
                INSERT INTO battle_skills (
                    battle_id, side, slot_index, skill_template_id, skill_tier, created_at
                ) VALUES (?, ?, ?, ?, ?, datetime('now'))
                """,
                [
                    row["battle_id"],
                    row["side"],
                    row["slot_index"],
                    row["skill_template_id"],
                    row.get("skill_tier"),
                ],
            )

        for row in temperature_rows:
            self._client.execute(
                """
                INSERT INTO battle_slot_temperatures (
                    battle_id, side, slot_index, temperature_state, created_at
                ) VALUES (?, ?, ?, ?, datetime('now'))
                """,
                [
                    row["battle_id"],
                    row["side"],
                    row["slot_index"],
                    row["temperature_state"],
                ],
            )
