from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class ProjectedBattleRows:
    battle_row: dict[str, object]
    card_rows: list[dict[str, object]]
    skill_rows: list[dict[str, object]]
    temperature_rows: list[dict[str, object]]
    card_template_ids: set[str]
    skill_template_ids: set[str]
