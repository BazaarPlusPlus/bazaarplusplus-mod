from __future__ import annotations


def build_replace_statements(battle_id: str) -> list[str]:
    return [
        f"DELETE FROM battle_cards WHERE battle_id = '{battle_id}'",
        f"DELETE FROM battle_skills WHERE battle_id = '{battle_id}'",
        f"DELETE FROM battle_slot_temperatures WHERE battle_id = '{battle_id}'",
    ]
