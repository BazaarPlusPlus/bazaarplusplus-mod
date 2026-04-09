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
