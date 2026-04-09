from analytics_sync.load.mysql_schema import TABLE_DDLS


def test_schema_contains_core_tables():
    ddl_blob = "\n".join(TABLE_DDLS)

    assert "CREATE TABLE runs" in ddl_blob
    assert "CREATE TABLE battles" in ddl_blob
    assert "CREATE TABLE card_templates" in ddl_blob
    assert "CREATE TABLE skill_templates" in ddl_blob
    assert "CREATE TABLE battle_cards" in ddl_blob
    assert "CREATE TABLE battle_skills" in ddl_blob
    assert "CREATE TABLE battle_slot_temperatures" in ddl_blob
    assert "CREATE TABLE sync_checkpoints" in ddl_blob
    assert "CREATE TABLE sync_run_tasks" in ddl_blob
    assert "CREATE TABLE sync_job_runs" in ddl_blob
