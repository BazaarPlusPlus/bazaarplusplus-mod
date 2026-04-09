from pathlib import Path

from analytics_sync.config import CloudflareConfig, MysqlConfig, R2Config, SyncConfig
from analytics_sync.load.provider import build_sql_client


def _base_config(**overrides: object) -> SyncConfig:
    return SyncConfig(
        sql_provider=overrides.get("sql_provider", "sqlite"),
        sqlite_path=overrides.get("sqlite_path", Path("/tmp/analytics.db")),
        mysql=overrides.get("mysql"),
        cloudflare=CloudflareConfig(api_token="token", account_id="account", d1_database_id="db"),
        r2=R2Config(account_id="r2-account", access_key_id="key", secret_access_key="secret", bucket="bucket"),
    )


def test_build_sql_client_returns_sqlite_client(tmp_path: Path):
    client = build_sql_client(_base_config(sql_provider="sqlite", sqlite_path=tmp_path / "analytics.db"))

    assert client.__class__.__name__ == "SqliteClient"


def test_build_sql_client_returns_mysql_client():
    client = build_sql_client(
        _base_config(
            sql_provider="mysql",
            sqlite_path=None,
            mysql=MysqlConfig(
                host="127.0.0.1",
                port=3306,
                user="tester",
                password="secret",
                database="analytics",
            ),
        )
    )

    assert client.__class__.__name__ == "MySqlClient"
