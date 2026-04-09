from analytics_sync.load.battle_repository import build_replace_statements


def test_build_replace_statements_deletes_old_battle_details_before_insert():
    statements = build_replace_statements("battle-1")

    assert statements[0].startswith("DELETE FROM battle_cards")
    assert statements[1].startswith("DELETE FROM battle_skills")
    assert statements[2].startswith("DELETE FROM battle_slot_temperatures")
