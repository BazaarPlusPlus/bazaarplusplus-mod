from __future__ import annotations

from dataclasses import dataclass

from analytics_sync.load.provider import SqlExecutor


@dataclass(frozen=True)
class SyncCursor:
    updated_at: str
    entity_id: str


class SyncCheckpointRepository:
    def __init__(self, client: SqlExecutor) -> None:
        self._client = client

    def get_cursor(self, source_name: str) -> SyncCursor:
        try:
            rows = self._client.fetch_all(
                """
                SELECT cursor_updated_at, cursor_entity_id
                FROM sync_checkpoints
                WHERE source_name = ?
                """,
                [source_name],
            )
        except Exception:
            return SyncCursor(updated_at="1970-01-01T00:00:00Z", entity_id="")
        if not rows:
            return SyncCursor(updated_at="1970-01-01T00:00:00Z", entity_id="")

        row = rows[0]
        updated_at = row.get("cursor_updated_at")
        entity_id = row.get("cursor_entity_id")
        if not isinstance(updated_at, str) or not isinstance(entity_id, str):
            return SyncCursor(updated_at="1970-01-01T00:00:00Z", entity_id="")
        return SyncCursor(updated_at=updated_at, entity_id=entity_id)

    def upsert_cursor(self, source_name: str, updated_at: str, entity_id: str) -> None:
        if self._client.dialect == "mysql":
            sql = """
            INSERT INTO sync_checkpoints (
                source_name, cursor_updated_at, cursor_entity_id, updated_at
            ) VALUES (?, ?, ?, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                cursor_updated_at = VALUES(cursor_updated_at),
                cursor_entity_id = VALUES(cursor_entity_id),
                updated_at = VALUES(updated_at)
            """
        else:
            sql = """
            INSERT INTO sync_checkpoints (
                source_name, cursor_updated_at, cursor_entity_id, updated_at
            ) VALUES (?, ?, ?, datetime('now'))
            ON CONFLICT(source_name) DO UPDATE SET
                cursor_updated_at = excluded.cursor_updated_at,
                cursor_entity_id = excluded.cursor_entity_id,
                updated_at = datetime('now')
            """
        self._client.execute(sql, [source_name, updated_at, entity_id])
