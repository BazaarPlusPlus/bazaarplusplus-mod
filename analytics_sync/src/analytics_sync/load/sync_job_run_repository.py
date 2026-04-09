from __future__ import annotations

from analytics_sync.load.sqlite_client import SqliteClient


class SyncJobRunRepository:
    def __init__(self, client: SqliteClient) -> None:
        self._client = client

    def insert_job_run(self, row: dict[str, object]) -> None:
        self._client.execute(
            """
            INSERT INTO sync_job_runs (
                job_name,
                provider,
                status,
                dry_run,
                started_at,
                finished_at,
                duration_ms,
                runs_seen,
                runs_written,
                battles_seen,
                battles_written,
                skipped_runs,
                pending_tasks,
                failed_tasks,
                checkpoint_source,
                checkpoint_updated_at,
                checkpoint_entity_id,
                error_message,
                created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))
            """,
            [
                row["job_name"],
                row["provider"],
                row["status"],
                row["dry_run"],
                row["started_at"],
                row["finished_at"],
                row["duration_ms"],
                row["runs_seen"],
                row["runs_written"],
                row["battles_seen"],
                row["battles_written"],
                row["skipped_runs"],
                row.get("pending_tasks", 0),
                row.get("failed_tasks", 0),
                row.get("checkpoint_source"),
                row.get("checkpoint_updated_at"),
                row.get("checkpoint_entity_id"),
                row.get("error_message"),
            ],
        )
