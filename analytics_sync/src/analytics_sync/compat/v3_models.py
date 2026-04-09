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
class RunProjection:
    run_id: str
    player_account_id: str
    status: str
    hero_id: str | None
    ended_at_utc: str


@dataclass(frozen=True)
class BattleProjection:
    battle_id: str
    run_id: str
    recorded_at_utc: str


@dataclass(frozen=True)
class V3SemanticBundle:
    run_projection: RunProjection
    battle_projections: list[BattleProjection]
    battle_components: dict[str, ParsedBattleComponents]
