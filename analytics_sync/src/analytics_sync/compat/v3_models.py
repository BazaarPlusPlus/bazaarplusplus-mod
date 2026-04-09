from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class CardComponent:
    side: str
    slot_index: int
    template_id: str
    card_tier: int | None
    enchant_code: str | None


@dataclass(frozen=True)
class SkillComponent:
    side: str
    slot_index: int
    template_id: str
    skill_tier: int | None


@dataclass(frozen=True)
class TemperatureComponent:
    side: str
    slot_index: int
    temperature_state: str


@dataclass(frozen=True)
class ParsedBattleComponents:
    cards: list[CardComponent]
    skills: list[SkillComponent]
    temperatures: list[TemperatureComponent]


@dataclass(frozen=True)
class RunBundleUploadRequestV2:
    run_projection: dict[str, object]
    battle_projections: list[dict[str, object]]
    battle_components: dict[str, ParsedBattleComponents]
