from analytics_sync.load.sqlite_schema import TABLE_DDLS


def test_sqlite_schema_contains_runs_and_battles():
    ddl_blob = "\n".join(TABLE_DDLS)

    assert "CREATE TABLE runs" in ddl_blob
    assert "CREATE TABLE battles" in ddl_blob
    assert "CREATE TABLE battle_cards" in ddl_blob
