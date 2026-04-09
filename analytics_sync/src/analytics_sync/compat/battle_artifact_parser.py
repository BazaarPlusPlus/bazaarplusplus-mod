from __future__ import annotations

from typing import cast

from analytics_sync.compat.v3_models import (
    CardComponent,
    ParsedBattleComponents,
    SkillComponent,
    TemperatureComponent,
)


def parse_battle_components(payload: dict[str, object]) -> ParsedBattleComponents:
    manifest = payload.get("battle_manifest")
    manifest_dict = _as_object_dict(manifest)
    if manifest_dict is not None:
        snapshots = manifest_dict.get("snapshots")
        snapshots_dict = _as_object_dict(snapshots)
        if snapshots_dict is not None:
            return _parse_snapshot_components(snapshots_dict)

    cards: list[CardComponent] = []
    skills: list[SkillComponent] = []
    temperatures: list[TemperatureComponent] = []

    for side in ("player", "opponent"):
        cards.extend(_parse_cards(side, payload.get(f"{side}_cards")))
        skills.extend(_parse_skills(side, payload.get(f"{side}_skills")))
        temperatures.extend(_parse_temperatures(side, payload.get(f"{side}_temperatures")))

    return ParsedBattleComponents(cards=cards, skills=skills, temperatures=temperatures)


def _parse_snapshot_components(snapshots: dict[str, object]) -> ParsedBattleComponents:
    cards: list[CardComponent] = []
    skills: list[SkillComponent] = []
    temperatures: list[TemperatureComponent] = []

    card_sources = {
        "player": snapshots.get("player_hand"),
        "opponent": snapshots.get("opponent_hand"),
    }
    skill_sources = {
        "player": snapshots.get("player_skills"),
        "opponent": snapshots.get("opponent_skills"),
    }

    for side, source in card_sources.items():
        source_dict = _as_object_dict(source)
        if source_dict is None:
            continue
        items = source_dict.get("items")
        if not isinstance(items, list):
            continue
        for item in items:
            item_dict = _as_object_dict(item)
            if item_dict is None:
                continue
            template_id = item_dict.get("template_id")
            slot_index = item_dict.get("socket")
            if not isinstance(template_id, str) or not isinstance(slot_index, int):
                continue
            cards.append(
                CardComponent(
                    side=side,
                    slot_index=slot_index,
                    template_id=template_id,
                    card_tier=_parse_tier(item_dict.get("tier")),
                    enchant_code=_parse_enchant(item_dict.get("enchant")),
                )
            )
            attributes_dict = _as_object_dict(item_dict.get("attributes"))
            if attributes_dict is not None:
                heated = attributes_dict.get("Heated")
                chilled = attributes_dict.get("Chilled")
                if isinstance(heated, int) and heated > 0:
                    temperatures.append(
                        TemperatureComponent(side=side, slot_index=slot_index, temperature_state="high")
                    )
                elif isinstance(chilled, int) and chilled > 0:
                    temperatures.append(
                        TemperatureComponent(side=side, slot_index=slot_index, temperature_state="low")
                    )

    for side, source in skill_sources.items():
        source_dict = _as_object_dict(source)
        if source_dict is None:
            continue
        items = source_dict.get("items")
        if not isinstance(items, list):
            continue
        for index, item in enumerate(items):
            item_dict = _as_object_dict(item)
            if item_dict is None:
                continue
            template_id = item_dict.get("template_id")
            if not isinstance(template_id, str):
                continue
            skills.append(
                SkillComponent(
                    side=side,
                    slot_index=index,
                    template_id=template_id,
                    skill_tier=_parse_tier(item_dict.get("tier")),
                )
            )

    return ParsedBattleComponents(cards=cards, skills=skills, temperatures=temperatures)


def _parse_cards(side: str, value: object) -> list[CardComponent]:
    if not isinstance(value, list):
        return []

    cards: list[CardComponent] = []
    for item in value:
        item_dict = _as_object_dict(item)
        if item_dict is None:
            continue
        template_id = item_dict.get("template_id")
        slot_index = item_dict.get("slot")
        if not isinstance(template_id, str) or not isinstance(slot_index, int):
            continue
        cards.append(
            CardComponent(
                side=side,
                slot_index=slot_index,
                template_id=template_id,
                card_tier=_parse_tier(item_dict.get("tier")),
                enchant_code=_parse_enchant(item_dict.get("enchant")),
            )
        )
    return cards


def _parse_skills(side: str, value: object) -> list[SkillComponent]:
    if not isinstance(value, list):
        return []

    skills: list[SkillComponent] = []
    for item in value:
        item_dict = _as_object_dict(item)
        if item_dict is None:
            continue
        template_id = item_dict.get("template_id")
        slot_index = item_dict.get("slot")
        if not isinstance(template_id, str) or not isinstance(slot_index, int):
            continue
        skills.append(
            SkillComponent(
                side=side,
                slot_index=slot_index,
                template_id=template_id,
                skill_tier=_parse_tier(item_dict.get("tier")),
            )
        )
    return skills


def _parse_temperatures(side: str, value: object) -> list[TemperatureComponent]:
    if not isinstance(value, list):
        return []

    temperatures: list[TemperatureComponent] = []
    for item in value:
        item_dict = _as_object_dict(item)
        if item_dict is None:
            continue
        slot_index = item_dict.get("slot")
        temperature_state = item_dict.get("state")
        if (
            not isinstance(slot_index, int)
            or not isinstance(temperature_state, str)
            or temperature_state not in {"high", "low"}
        ):
            continue
        temperatures.append(
            TemperatureComponent(
                side=side,
                slot_index=slot_index,
                temperature_state=temperature_state,
            )
        )
    return temperatures


def _parse_tier(value: object) -> int | None:
    if isinstance(value, int):
        return value
    if not isinstance(value, str):
        return None
    mapping = {
        "Bronze": 1,
        "Silver": 2,
        "Gold": 3,
        "Diamond": 4,
        "Legendary": 5,
    }
    return mapping.get(value)


def _parse_enchant(value: object) -> str | None:
    if not isinstance(value, str):
        return None
    return value or None


def _as_object_dict(value: object) -> dict[str, object] | None:
    if not isinstance(value, dict):
        return None
    return cast(dict[str, object], value)
