from pathlib import Path

from analytics_sync.load.sqlite_client import SqliteClient
from analytics_sync.load.sync_job_run_repository import SyncJobRunRepository


def test_sync_job_run_repository_inserts_summary_row(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    repository = SyncJobRunRepository(client)

    repository.insert_job_run(
        {
            "job_name": "sync_once",
            "provider": "sqlite",
            "status": "success",
            "dry_run": 0,
            "started_at": "2026-04-10T00:00:00Z",
            "finished_at": "2026-04-10T00:00:03Z",
            "duration_ms": 3000,
            "runs_seen": 3,
            "runs_written": 2,
            "battles_seen": 8,
            "battles_written": 8,
            "skipped_runs": 1,
            "pending_tasks": 3,
            "failed_tasks": 2,
            "checkpoint_source": "runs_d1",
            "checkpoint_updated_at": "2026-04-10T00:00:00Z",
            "checkpoint_entity_id": "run-42",
            "error_message": None,
        }
    )

    rows = client.fetch_all(
        """
        SELECT job_name, provider, status, runs_written, pending_tasks, failed_tasks, checkpoint_entity_id
        FROM sync_job_runs
        """,
    )
    assert rows == [
        {
            "job_name": "sync_once",
            "provider": "sqlite",
            "status": "success",
            "runs_written": 2,
            "pending_tasks": 3,
            "failed_tasks": 2,
            "checkpoint_entity_id": "run-42",
        }
    ]
