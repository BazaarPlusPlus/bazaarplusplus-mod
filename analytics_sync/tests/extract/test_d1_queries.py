from analytics_sync.extract.d1_queries import (
    build_battles_for_run_ids_query,
    build_battles_query,
    build_runs_query,
)


def test_build_runs_query_orders_by_updated_at_and_run_id():
    sql = build_runs_query()

    assert "FROM runs" in sql
    assert "ORDER BY updated_at_utc, run_id" in sql
    assert "LIMIT ?" in sql
    assert "updated_at_utc > ?" in sql


def test_build_battles_query_orders_by_updated_at_and_battle_id():
    sql = build_battles_query()

    assert "FROM battles" in sql
    assert "ORDER BY updated_at_utc, battle_id" in sql
    assert "LIMIT ?" in sql
    assert "updated_at_utc > ?" in sql


def test_build_battles_for_run_ids_query_uses_in_clause():
    sql = build_battles_for_run_ids_query(3)

    assert "FROM battles" in sql
    assert "WHERE run_id IN (?,?,?)" in sql
    assert "ORDER BY run_id, recorded_at_utc, battle_id" in sql
