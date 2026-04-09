from __future__ import annotations

from analytics_sync.transform.row_models import ProjectedBattleRows


def project_battle_rows(
    *,
    battle_projection: dict[str, object],
    cards: list[dict[str, object]],
    skills: list[dict[str, object]],
    temperatures: list[dict[str, object]],
) -> ProjectedBattleRows:
    battle_id = battle_projection.get("battle_id")
    run_id = battle_projection.get("run_id")
    recorded_at_utc = battle_projection.get("recorded_at_utc")

    if not isinstance(battle_id, str):
        raise ValueError("battle_id is required")
    if not isinstance(run_id, str):
        raise ValueError("run_id is required")
    if not isinstance(recorded_at_utc, str):
        raise ValueError("recorded_at_utc is required")

    card_rows = [
        {
            "battle_id": battle_id,
            "side": card["side"],
            "slot_index": card["slot_index"],
            "template_id": card["template_id"],
            "card_tier": card.get("card_tier"),
            "enchant_code": card.get("enchant_code"),
        }
        for card in cards
    ]
    skill_rows = [
        {
            "battle_id": battle_id,
            "side": skill["side"],
            "slot_index": skill["slot_index"],
            "template_id": skill["template_id"],
            "skill_tier": skill.get("skill_tier"),
        }
        for skill in skills
    ]
    temperature_rows = [
        {
            "battle_id": battle_id,
            "side": temperature["side"],
            "slot_index": temperature["slot_index"],
            "temperature_state": temperature["temperature_state"],
        }
        for temperature in temperatures
    ]

    return ProjectedBattleRows(
        battle_row={
            "battle_id": battle_id,
            "run_id": run_id,
            "recorded_at_utc": recorded_at_utc,
        },
        card_rows=card_rows,
        skill_rows=skill_rows,
        temperature_rows=temperature_rows,
        card_template_ids={
            template_id
            for template_id in (card.get("template_id") for card in cards)
            if isinstance(template_id, str)
        },
        skill_template_ids={
            template_id
            for template_id in (skill.get("template_id") for skill in skills)
            if isinstance(template_id, str)
        },
    )
