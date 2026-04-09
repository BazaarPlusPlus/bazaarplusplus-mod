from __future__ import annotations

from analytics_sync.load.sqlite_client import SqliteClient


class RunRepository:
    def __init__(self, client: SqliteClient) -> None:
        self._client = client

    def upsert(self, row: dict[str, object]) -> None:
        self._client.execute(
            """
            INSERT INTO runs (
                run_id,
                installation_id,
                player_account_id,
                plugin_version,
                game_version,
                submitted_at_utc,
                status,
                hero_id,
                hero_name,
                player_rank,
                player_rating,
                player_position,
                started_at_utc,
                ended_at_utc,
                final_day,
                final_wins,
                final_losses,
                final_player_rank,
                final_player_rating,
                final_player_position,
                created_at,
                updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'), datetime('now'))
            ON CONFLICT(run_id) DO UPDATE SET
                installation_id = excluded.installation_id,
                player_account_id = excluded.player_account_id,
                plugin_version = excluded.plugin_version,
                submitted_at_utc = excluded.submitted_at_utc,
                status = excluded.status,
                hero_id = excluded.hero_id,
                hero_name = excluded.hero_name,
                player_rank = excluded.player_rank,
                player_rating = excluded.player_rating,
                player_position = excluded.player_position,
                ended_at_utc = excluded.ended_at_utc,
                final_day = excluded.final_day,
                final_wins = excluded.final_wins,
                final_losses = excluded.final_losses,
                final_player_rank = excluded.final_player_rank,
                final_player_rating = excluded.final_player_rating,
                final_player_position = excluded.final_player_position,
                updated_at = datetime('now')
            """,
            [
                row["run_id"],
                row.get("installation_id"),
                row["player_account_id"],
                row.get("plugin_version"),
                row.get("game_version"),
                row.get("submitted_at_utc"),
                row["status"],
                row.get("hero_id"),
                row.get("hero_name"),
                row.get("player_rank"),
                row.get("player_rating"),
                row.get("player_position"),
                row.get("started_at_utc"),
                row["ended_at_utc"],
                row.get("final_day"),
                row.get("final_wins"),
                row.get("final_losses"),
                row.get("final_player_rank"),
                row.get("final_player_rating"),
                row.get("final_player_position"),
            ],
        )
