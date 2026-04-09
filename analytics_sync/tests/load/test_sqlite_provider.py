from pathlib import Path

from analytics_sync.load.sqlite_client import SqliteClient


def test_sqlite_client_initializes_runs_table(tmp_path: Path):
    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)

    client.initialize_schema()

    tables = client.fetch_all(
        "SELECT name FROM sqlite_master WHERE type = 'table' AND name = ?",
        ["runs"],
    )
    assert tables[0]["name"] == "runs"
