from __future__ import annotations

from analytics_sync.load.sqlite_client import SqliteClient


class RunTaskRepository:
    def __init__(self, client: SqliteClient) -> None:
        self._client = client

    def upsert_task(
        self,
        task_type: str,
        run_id: str,
        status: str,
        next_run_at: str,
    ) -> None:
        self._client.execute(
            """
            INSERT INTO sync_run_tasks (
                task_type, run_id, status, attempt_count, next_run_at, last_error, created_at, updated_at
            ) VALUES (?, ?, ?, 0, ?, NULL, ?, ?)
            ON CONFLICT(task_type, run_id) DO UPDATE SET
                status = excluded.status,
                next_run_at = excluded.next_run_at,
                updated_at = excluded.updated_at
            """,
            [task_type, run_id, status, next_run_at, next_run_at, next_run_at],
        )
