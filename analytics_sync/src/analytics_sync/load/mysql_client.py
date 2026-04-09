from __future__ import annotations

from collections.abc import Iterator
from contextlib import contextmanager
from datetime import UTC, datetime
import re
from typing import Any, cast

import mysql.connector
from mysql.connector.abstracts import MySQLConnectionAbstract

from analytics_sync.config import MysqlConfig
from analytics_sync.load.mysql_schema import TABLE_DDLS

_ISO_TIMESTAMP_PATTERN = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}")


def _to_mysql_param(value: object) -> object:
    if not isinstance(value, str) or not _ISO_TIMESTAMP_PATTERN.match(value):
        return value
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(UTC)
    return parsed.replace(tzinfo=None)


def _to_python_value(value: object) -> object:
    if isinstance(value, datetime):
        return value.replace(tzinfo=UTC).isoformat().replace("+00:00", "Z")
    return value


class _MySqlSession:
    dialect = "mysql"

    def __init__(self, connection: MySQLConnectionAbstract) -> None:
        self._connection = connection

    def execute(self, sql: str, params: list[object] | None = None) -> None:
        cursor = self._connection.cursor()
        try:
            mysql_params = cast(Any, [_to_mysql_param(param) for param in (params or [])])
            cursor.execute(sql.replace("?", "%s"), mysql_params)
        finally:
            cursor.close()

    def executemany(self, sql: str, params_seq: list[list[object]]) -> None:
        if not params_seq:
            return
        cursor = self._connection.cursor()
        try:
            mysql_params = cast(Any, [[_to_mysql_param(param) for param in params] for params in params_seq])
            cursor.executemany(
                sql.replace("?", "%s"),
                mysql_params,
            )
        finally:
            cursor.close()

    def fetch_all(self, sql: str, params: list[object] | None = None) -> list[dict[str, object]]:
        cursor = self._connection.cursor(dictionary=True)
        try:
            mysql_params = cast(Any, [_to_mysql_param(param) for param in (params or [])])
            cursor.execute(sql.replace("?", "%s"), mysql_params)
            rows = cast(list[dict[str, object]], cursor.fetchall())
        finally:
            cursor.close()
        return [{key: _to_python_value(value) for key, value in row.items()} for row in rows]


class MySqlClient:
    dialect = "mysql"

    def __init__(self, config: MysqlConfig) -> None:
        self._config = config

    def connect(self) -> MySQLConnectionAbstract:
        return cast(
            MySQLConnectionAbstract,
            mysql.connector.connect(
            host=self._config.host,
            port=self._config.port,
            user=self._config.user,
            password=self._config.password,
            database=self._config.database,
            autocommit=False,
            ),
        )

    def initialize_schema(self) -> None:
        connection = self.connect()
        try:
            session = _MySqlSession(connection)
            for ddl in TABLE_DDLS:
                session.execute(ddl.replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", 1))
            connection.commit()
        finally:
            connection.close()

    @contextmanager
    def transaction(self) -> Iterator[_MySqlSession]:
        connection = self.connect()
        try:
            session = _MySqlSession(connection)
            yield session
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
        connection = self.connect()
        try:
            return _MySqlSession(connection).fetch_all(sql, params)
        finally:
            connection.close()
