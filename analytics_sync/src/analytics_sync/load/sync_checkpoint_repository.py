from __future__ import annotations

from dataclasses import dataclass
import sqlite3

from analytics_sync.load.sqlite_client import SqliteClient


@dataclass(frozen=True)
class SyncCursor:
    updated_at: str
    entity_id: str


class SyncCheckpointRepository:
    def __init__(self, client: SqliteClient) -> None:
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
        except sqlite3.OperationalError:
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
        self._client.execute(
            """
            INSERT INTO sync_checkpoints (
                source_name, cursor_updated_at, cursor_entity_id, updated_at
            ) VALUES (?, ?, ?, datetime('now'))
            ON CONFLICT(source_name) DO UPDATE SET
                cursor_updated_at = excluded.cursor_updated_at,
                cursor_entity_id = excluded.cursor_entity_id,
                updated_at = datetime('now')
            """,
            [source_name, updated_at, entity_id],
        )
