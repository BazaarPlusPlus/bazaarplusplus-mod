from __future__ import annotations

from datetime import UTC, datetime

from analytics_sync.load.provider import SqlExecutor


def _utc_now() -> str:
    return datetime.now(UTC).isoformat().replace("+00:00", "Z")


class RunRepository:
    def __init__(self, client: SqlExecutor) -> None:
        self._client = client

    def upsert(self, row: dict[str, object]) -> None:
        now = _utc_now()
        if self._client.dialect == "mysql":
            sql = """
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
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON DUPLICATE KEY UPDATE
                installation_id = VALUES(installation_id),
                player_account_id = VALUES(player_account_id),
                plugin_version = VALUES(plugin_version),
                game_version = VALUES(game_version),
                submitted_at_utc = VALUES(submitted_at_utc),
                status = VALUES(status),
                hero_id = VALUES(hero_id),
                hero_name = VALUES(hero_name),
                player_rank = VALUES(player_rank),
                player_rating = VALUES(player_rating),
                player_position = VALUES(player_position),
                started_at_utc = VALUES(started_at_utc),
                ended_at_utc = VALUES(ended_at_utc),
                final_day = VALUES(final_day),
                final_wins = VALUES(final_wins),
                final_losses = VALUES(final_losses),
                final_player_rank = VALUES(final_player_rank),
                final_player_rating = VALUES(final_player_rating),
                final_player_position = VALUES(final_player_position),
                updated_at = VALUES(updated_at)
            """
        else:
            sql = """
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
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(run_id) DO UPDATE SET
                installation_id = excluded.installation_id,
                player_account_id = excluded.player_account_id,
                plugin_version = excluded.plugin_version,
                game_version = excluded.game_version,
                submitted_at_utc = excluded.submitted_at_utc,
                status = excluded.status,
                hero_id = excluded.hero_id,
                hero_name = excluded.hero_name,
                player_rank = excluded.player_rank,
                player_rating = excluded.player_rating,
                player_position = excluded.player_position,
                started_at_utc = excluded.started_at_utc,
                ended_at_utc = excluded.ended_at_utc,
                final_day = excluded.final_day,
                final_wins = excluded.final_wins,
                final_losses = excluded.final_losses,
                final_player_rank = excluded.final_player_rank,
                final_player_rating = excluded.final_player_rating,
                final_player_position = excluded.final_player_position,
                updated_at = excluded.updated_at
            """
        self._client.execute(
            sql,
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
                now,
                now,
            ],
        )
