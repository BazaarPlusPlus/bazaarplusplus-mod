from pathlib import Path

from analytics_sync.load.run_task_repository import RunTaskRepository
from analytics_sync.load.sqlite_client import SqliteClient


def test_run_task_repository_upserts_by_task_type_and_run_id(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = RunTaskRepository(client)

    repository.upsert_task("sync_run", "run-1", "pending", "2026-04-09T00:00:00Z")
    repository.upsert_task("sync_run", "run-1", "pending", "2026-04-09T00:00:00Z")

    rows = client.fetch_all(
        "SELECT task_type, run_id FROM sync_run_tasks WHERE run_id = ?",
        ["run-1"],
    )
    assert len(rows) == 1
    assert rows[0]["task_type"] == "sync_run"
