from __future__ import annotations

from analytics_sync.compat.v3_models import (
    CardComponent,
    ParsedBattleComponents,
    SkillComponent,
    TemperatureComponent,
)


def parse_battle_components(payload: dict[str, object]) -> ParsedBattleComponents:
    cards: list[CardComponent] = []
    skills: list[SkillComponent] = []
    temperatures: list[TemperatureComponent] = []

    for side in ("player", "opponent"):
        cards.extend(_parse_cards(side, payload.get(f"{side}_cards")))
        skills.extend(_parse_skills(side, payload.get(f"{side}_skills")))
        temperatures.extend(_parse_temperatures(side, payload.get(f"{side}_temperatures")))

    return ParsedBattleComponents(cards=cards, skills=skills, temperatures=temperatures)


def _parse_cards(side: str, value: object) -> list[CardComponent]:
    if not isinstance(value, list):
        return []

    cards: list[CardComponent] = []
    for item in value:
        if not isinstance(item, dict):
            continue
        template_id = item.get("template_id")
        slot_index = item.get("slot")
        if not isinstance(template_id, str) or not isinstance(slot_index, int):
            continue
        cards.append(
            CardComponent(
                side=side,
                slot_index=slot_index,
                template_id=template_id,
                card_tier=item.get("tier") if isinstance(item.get("tier"), int) else None,
                enchant_code=item.get("enchant") if isinstance(item.get("enchant"), str) else None,
            )
        )
    return cards


def _parse_skills(side: str, value: object) -> list[SkillComponent]:
    if not isinstance(value, list):
        return []

    skills: list[SkillComponent] = []
    for item in value:
        if not isinstance(item, dict):
            continue
        template_id = item.get("template_id")
        slot_index = item.get("slot")
        if not isinstance(template_id, str) or not isinstance(slot_index, int):
            continue
        skills.append(
            SkillComponent(
                side=side,
                slot_index=slot_index,
                template_id=template_id,
                skill_tier=item.get("tier") if isinstance(item.get("tier"), int) else None,
            )
        )
    return skills


def _parse_temperatures(side: str, value: object) -> list[TemperatureComponent]:
    if not isinstance(value, list):
        return []

    temperatures: list[TemperatureComponent] = []
    for item in value:
        if not isinstance(item, dict):
            continue
        slot_index = item.get("slot")
        temperature_state = item.get("state")
        if not isinstance(slot_index, int) or temperature_state not in {"high", "low"}:
            continue
        temperatures.append(
            TemperatureComponent(
                side=side,
                slot_index=slot_index,
                temperature_state=temperature_state,
            )
        )
    return temperatures
