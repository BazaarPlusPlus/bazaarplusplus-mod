from __future__ import annotations

from pathlib import Path
import sqlite3

from analytics_sync.load.sqlite_schema import TABLE_DDLS


class SqliteClient:
    def __init__(self, database_path: Path) -> None:
        self._database_path = database_path

    def connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self._database_path)
        connection.row_factory = sqlite3.Row
        return connection

    def initialize_schema(self) -> None:
        with self.connect() as connection:
            for ddl in TABLE_DDLS:
                connection.execute(ddl.replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", 1))

    def execute(self, sql: str, params: list[object] | None = None) -> None:
        with self.connect() as connection:
            connection.execute(sql, params or [])

    def fetch_all(self, sql: str, params: list[object] | None = None) -> list[dict[str, object]]:
        with self.connect() as connection:
            rows = connection.execute(sql, params or []).fetchall()
        return [dict(row) for row in rows]
