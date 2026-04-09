from analytics_sync.transform.project_battle import project_battle_rows


def test_project_battle_rows_keeps_card_template_identity():
    rows = project_battle_rows(
        battle_projection={
            "battle_id": "battle-1",
            "run_id": "run-1",
            "recorded_at_utc": "2026-04-09T00:00:00Z",
        },
        cards=[
            {
                "side": "player",
                "slot_index": 0,
                "template_id": "card-a",
                "card_tier": 2,
                "enchant_code": "burning",
            }
        ],
        skills=[],
        temperatures=[],
    )

    assert rows.card_template_ids == {"card-a"}
    assert rows.card_rows[0]["slot_index"] == 0
