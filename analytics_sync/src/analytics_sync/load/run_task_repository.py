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

    def list_run_ids(
        self,
        task_type: str,
        limit: int,
        *,
        ready_at: str,
        stale_before: str,
    ) -> list[str]:
        rows = self._client.fetch_all(
            """
            SELECT run_id
            FROM sync_run_tasks
            WHERE task_type = ?
              AND (
                (status IN ('pending', 'failed') AND next_run_at <= ?)
                OR
                (status = 'running' AND updated_at <= ?)
              )
            ORDER BY
              CASE status
                WHEN 'running' THEN 0
                ELSE 1
              END,
              next_run_at,
              id
            LIMIT ?
            """,
            [task_type, ready_at, stale_before, limit],
        )
        run_ids: list[str] = []
        for row in rows:
            run_id = row.get("run_id")
            if isinstance(run_id, str):
                run_ids.append(run_id)
        return run_ids

    def claim_run_ids(
        self,
        task_type: str,
        limit: int,
        *,
        claimed_at: str,
        stale_before: str,
    ) -> list[str]:
        run_ids = self.list_run_ids(
            task_type,
            limit,
            ready_at=claimed_at,
            stale_before=stale_before,
        )
        for run_id in run_ids:
            self._client.execute(
                """
                UPDATE sync_run_tasks
                SET status = 'running',
                    attempt_count = attempt_count + 1,
                    updated_at = ?
                WHERE task_type = ?
                  AND run_id = ?
                """,
                [claimed_at, task_type, run_id],
            )
        return run_ids

    def mark_succeeded(self, task_type: str, run_id: str, finished_at: str) -> None:
        self._client.execute(
            """
            UPDATE sync_run_tasks
            SET status = 'succeeded',
                last_error = NULL,
                next_run_at = ?,
                updated_at = ?
            WHERE task_type = ?
              AND run_id = ?
            """,
            [finished_at, finished_at, task_type, run_id],
        )

    def mark_failed(self, task_type: str, run_id: str, failed_at: str, error_message: str) -> None:
        self._client.execute(
            """
            UPDATE sync_run_tasks
            SET status = 'failed',
                last_error = ?,
                next_run_at = ?,
                updated_at = ?
            WHERE task_type = ?
              AND run_id = ?
            """,
            [error_message, failed_at, failed_at, task_type, run_id],
        )

    def count_by_status(self, task_type: str, status: str) -> int:
        rows = self._client.fetch_all(
            """
            SELECT COUNT(*) AS count
            FROM sync_run_tasks
            WHERE task_type = ?
              AND status = ?
            """,
            [task_type, status],
        )
        count = rows[0].get("count") if rows else 0
        return count if isinstance(count, int) else 0
