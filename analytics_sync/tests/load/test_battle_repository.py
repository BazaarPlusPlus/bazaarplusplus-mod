from analytics_sync.load.battle_repository import build_replace_statements
from analytics_sync.load.sqlite_client import SqliteClient
from analytics_sync.load.template_repository import TemplateRepository
from pathlib import Path


def test_build_replace_statements_deletes_old_battle_details_before_insert():
    statements = build_replace_statements("battle-1")

    assert statements[0].startswith("DELETE FROM battle_cards")
    assert statements[1].startswith("DELETE FROM battle_skills")
    assert statements[2].startswith("DELETE FROM battle_slot_temperatures")


def test_battle_repository_writes_battle_details(tmp_path: Path):
    from analytics_sync.load.battle_repository import BattleRepository

    database_path = tmp_path / "analytics.db"
    client = SqliteClient(database_path)
    client.initialize_schema()
    templates = TemplateRepository(client)
    card_ids = templates.upsert_card_templates({"card-a"})
    skill_ids = templates.upsert_skill_templates({"skill-a"})
    repository = BattleRepository(client)

    repository.upsert_battle(
        battle_row={
            "battle_id": "battle-1",
            "run_id": "run-1",
            "recorded_at_utc": "2026-04-09T00:00:00Z",
        },
        card_rows=[
            {
                "battle_id": "battle-1",
                "side": "player",
                "slot_index": 0,
                "card_template_id": card_ids["card-a"],
                "card_tier": 2,
                "enchant_code": "burning",
            }
        ],
        skill_rows=[
            {
                "battle_id": "battle-1",
                "side": "player",
                "slot_index": 0,
                "skill_template_id": skill_ids["skill-a"],
                "skill_tier": 4,
            }
        ],
        temperature_rows=[
            {
                "battle_id": "battle-1",
                "side": "player",
                "slot_index": 0,
                "temperature_state": "high",
            }
        ],
    )

    assert client.fetch_all("SELECT battle_id FROM battles WHERE battle_id = ?", ["battle-1"])
    assert client.fetch_all("SELECT battle_id FROM battle_cards WHERE battle_id = ?", ["battle-1"])
