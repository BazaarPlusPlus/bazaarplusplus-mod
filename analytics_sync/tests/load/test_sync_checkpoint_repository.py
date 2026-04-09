from pathlib import Path

from analytics_sync.load.sqlite_client import SqliteClient
from analytics_sync.load.sync_checkpoint_repository import SyncCheckpointRepository


def test_sync_checkpoint_repository_returns_default_cursor(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = SyncCheckpointRepository(client)

    cursor = repository.get_cursor("runs_d1")

    assert cursor.updated_at == "1970-01-01T00:00:00Z"
    assert cursor.entity_id == ""


def test_sync_checkpoint_repository_returns_default_when_schema_missing(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    repository = SyncCheckpointRepository(client)

    cursor = repository.get_cursor("runs_d1")

    assert cursor.updated_at == "1970-01-01T00:00:00Z"
    assert cursor.entity_id == ""


def test_sync_checkpoint_repository_upserts_cursor(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = SyncCheckpointRepository(client)

    repository.upsert_cursor("runs_d1", "2026-04-09T00:00:00Z", "run-1")

    cursor = repository.get_cursor("runs_d1")
    assert cursor.updated_at == "2026-04-09T00:00:00Z"
    assert cursor.entity_id == "run-1"
