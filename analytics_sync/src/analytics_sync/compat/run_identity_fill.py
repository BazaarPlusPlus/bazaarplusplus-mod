from __future__ import annotations


def fill_run_player_account_id(
    run_row: dict[str, object],
    battle_rows: list[dict[str, object]],
) -> dict[str, object]:
    if run_row.get("player_account_id"):
        return dict(run_row)

    for battle_row in battle_rows:
        candidate = battle_row.get("player_account_id")
        if battle_row.get("run_id") == run_row.get("run_id") and isinstance(candidate, str):
            filled = dict(run_row)
            filled["player_account_id"] = candidate
            return filled

    return dict(run_row)
