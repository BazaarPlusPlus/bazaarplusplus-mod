from analytics_sync.compat.run_identity_fill import fill_run_player_account_id


def test_fill_run_player_account_id_uses_related_battle_when_summary_missing():
    run_row = {"run_id": "run-1", "player_account_id": None}
    battle_rows = [{"run_id": "run-1", "player_account_id": "player-123"}]

    filled = fill_run_player_account_id(run_row, battle_rows)

    assert filled["player_account_id"] == "player-123"
