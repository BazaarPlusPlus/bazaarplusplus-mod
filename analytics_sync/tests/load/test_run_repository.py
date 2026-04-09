from pathlib import Path

from analytics_sync.load.run_repository import RunRepository
from analytics_sync.load.sqlite_client import SqliteClient


def test_run_repository_upserts_run_row(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = RunRepository(client)

    repository.upsert(
        {
            "run_id": "run-1",
            "player_account_id": "player-123",
            "status": "completed",
            "hero_id": None,
            "ended_at_utc": "2026-04-09T00:00:00Z",
        }
    )

    rows = client.fetch_all("SELECT run_id, player_account_id FROM runs WHERE run_id = ?", ["run-1"])
    assert rows[0]["run_id"] == "run-1"
    assert rows[0]["player_account_id"] == "player-123"


def test_run_repository_updates_rank_fields_on_conflict(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = RunRepository(client)

    repository.upsert(
        {
            "run_id": "run-1",
            "player_account_id": "player-123",
            "status": "completed",
            "player_rank": "Silver",
            "player_rating": 1200,
            "final_player_rank": "Gold",
            "final_player_rating": 1337,
            "ended_at_utc": "2026-04-09T00:00:00Z",
        }
    )
    repository.upsert(
        {
            "run_id": "run-1",
            "player_account_id": "player-123",
            "status": "completed",
            "player_rank": "Legend",
            "player_rating": 1500,
            "final_player_rank": "Mythic",
            "final_player_rating": 1666,
            "ended_at_utc": "2026-04-10T00:00:00Z",
        }
    )

    rows = client.fetch_all(
        """
        SELECT player_rank, player_rating, final_player_rank, final_player_rating
        FROM runs
        WHERE run_id = ?
        """,
        ["run-1"],
    )
    assert rows[0]["player_rank"] == "Legend"
    assert rows[0]["player_rating"] == 1500
    assert rows[0]["final_player_rank"] == "Mythic"
    assert rows[0]["final_player_rating"] == 1666
