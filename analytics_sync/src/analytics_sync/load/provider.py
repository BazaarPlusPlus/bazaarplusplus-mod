from __future__ import annotations

from contextlib import AbstractContextManager
from typing import Protocol

from analytics_sync.config import SyncConfig
from analytics_sync.load.mysql_client import MySqlClient
from analytics_sync.load.sqlite_client import SqliteClient


class SqlExecutor(Protocol):
    dialect: str

    def execute(self, sql: str, params: list[object] | None = None) -> None: ...

    def executemany(self, sql: str, params_seq: list[list[object]]) -> None: ...

    def fetch_all(self, sql: str, params: list[object] | None = None) -> list[dict[str, object]]: ...


class SqlClient(SqlExecutor, Protocol):
    def initialize_schema(self) -> None: ...

    def transaction(self) -> AbstractContextManager[SqlExecutor]: ...


def build_sql_client(config: SyncConfig) -> SqlClient:
    if config.sql_provider == "sqlite":
        if config.sqlite_path is None:
            raise ValueError("sqlite_path is required for sqlite provider")
        return SqliteClient(config.sqlite_path)

    if config.sql_provider == "mysql":
        if config.mysql is None:
            raise ValueError("mysql config is required for mysql provider")
        return MySqlClient(config.mysql)

    raise ValueError(f"Unsupported sql provider: {config.sql_provider}")
