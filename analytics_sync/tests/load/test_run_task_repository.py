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


def test_run_task_repository_lists_pending_run_ids(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = RunTaskRepository(client)

    repository.upsert_task("sync_run", "run-1", "pending", "2026-04-09T00:00:00Z")
    repository.upsert_task("sync_run", "run-2", "failed", "2026-04-09T00:00:01Z")
    repository.upsert_task("sync_run", "run-3", "succeeded", "2026-04-09T00:00:02Z")

    run_ids = repository.list_run_ids(
        "sync_run",
        limit=10,
        ready_at="2026-04-09T00:00:05Z",
        stale_before="2026-04-08T23:00:00Z",
    )

    assert run_ids == ["run-1", "run-2"]


def test_run_task_repository_counts_tasks_by_status(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = RunTaskRepository(client)

    repository.upsert_task("sync_run", "run-1", "pending", "2026-04-09T00:00:00Z")
    repository.upsert_task("sync_run", "run-2", "failed", "2026-04-09T00:00:01Z")
    repository.upsert_task("sync_run", "run-3", "succeeded", "2026-04-09T00:00:02Z")

    assert repository.count_by_status("sync_run", "pending") == 1
    assert repository.count_by_status("sync_run", "failed") == 1


def test_run_task_repository_reclaims_stale_running_tasks(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = RunTaskRepository(client)

    repository.upsert_task("sync_run", "run-stale", "running", "2026-04-09T00:00:00Z")
    repository.upsert_task("sync_run", "run-fresh", "running", "2026-04-09T00:00:00Z")
    client.execute(
        """
        UPDATE sync_run_tasks
        SET updated_at = ?
        WHERE run_id = ?
        """,
        ["2026-04-09T00:00:00Z", "run-stale"],
    )
    client.execute(
        """
        UPDATE sync_run_tasks
        SET updated_at = ?
        WHERE run_id = ?
        """,
        ["2026-04-09T00:20:00Z", "run-fresh"],
    )

    run_ids = repository.claim_run_ids(
        "sync_run",
        limit=10,
        claimed_at="2026-04-09T00:30:00Z",
        stale_before="2026-04-09T00:15:00Z",
    )

    assert run_ids == ["run-stale"]
