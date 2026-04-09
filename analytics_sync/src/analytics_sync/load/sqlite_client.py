from __future__ import annotations

from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path
import sqlite3

from analytics_sync.load.sqlite_schema import TABLE_DDLS


class _SqliteSession:
    dialect = "sqlite"

    def __init__(self, connection: sqlite3.Connection) -> None:
        self._connection = connection

    def execute(self, sql: str, params: list[object] | None = None) -> None:
        self._connection.execute(sql, params or [])

    def executemany(self, sql: str, params_seq: list[list[object]]) -> None:
        if not params_seq:
            return
        self._connection.executemany(sql, params_seq)

    def fetch_all(self, sql: str, params: list[object] | None = None) -> list[dict[str, object]]:
        rows = self._connection.execute(sql, params or []).fetchall()
        return [dict(row) for row in rows]


class SqliteClient:
    dialect = "sqlite"

    def __init__(self, database_path: Path) -> None:
        self._database_path = database_path

    def connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self._database_path)
        connection.row_factory = sqlite3.Row
        return connection

    def initialize_schema(self) -> None:
        with self.connect() as connection:
            connection.execute("PRAGMA journal_mode=WAL")
            connection.execute("PRAGMA synchronous=NORMAL")
            for ddl in TABLE_DDLS:
                connection.execute(ddl.replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", 1))

    @contextmanager
    def transaction(self) -> Iterator[_SqliteSession]:
        connection = self.connect()
        try:
            yield _SqliteSession(connection)
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    def execute(self, sql: str, params: list[object] | None = None) -> None:
        with self.transaction() as session:
            session.execute(sql, params)

    def executemany(self, sql: str, params_seq: list[list[object]]) -> None:
        with self.transaction() as session:
            session.executemany(sql, params_seq)

    def fetch_all(self, sql: str, params: list[object] | None = None) -> list[dict[str, object]]:
        with self.connect() as connection:
            rows = connection.execute(sql, params or []).fetchall()
        return [dict(row) for row in rows]
