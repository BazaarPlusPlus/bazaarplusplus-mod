from analytics_sync.compat.battle_artifact_parser import parse_battle_components


def test_parse_battle_components_extracts_cards_skills_and_temperatures():
    result = parse_battle_components(
        {
            "player_cards": [
                {"template_id": "card-a", "slot": 0, "tier": 2, "enchant": "burning"}
            ],
            "player_skills": [{"template_id": "skill-a", "slot": 0, "tier": 1}],
            "player_temperatures": [{"slot": 1, "state": "high"}],
        }
    )

    assert result.cards[0].template_id == "card-a"
    assert result.cards[0].card_tier == 2
    assert result.skills[0].template_id == "skill-a"
    assert result.temperatures[0].temperature_state == "high"


def test_parse_battle_components_supports_snapshot_shape():
    result = parse_battle_components(
        {
            "battle_manifest": {
                "snapshots": {
                    "player_hand": {
                        "items": [
                            {
                                "template_id": "card-a",
                                "socket": 6,
                                "tier": "Bronze",
                                "enchant": "",
                                "attributes": {"Heated": 1, "Chilled": 0},
                            }
                        ]
                    },
                    "player_skills": {
                        "items": [
                            {
                                "template_id": "skill-a",
                                "tier": "Diamond",
                            }
                        ]
                    },
                    "opponent_hand": {"items": []},
                    "opponent_skills": {"items": []},
                }
            }
        }
    )

    assert result.cards[0].slot_index == 6
    assert result.cards[0].card_tier == 1
    assert result.skills[0].skill_tier == 4
    assert result.temperatures[0].temperature_state == "high"
