from __future__ import annotations

from analytics_sync.compat.battle_artifact_parser import parse_battle_components
from analytics_sync.compat.v3_models import (
    BattleProjection,
    ParsedBattleComponents,
    RunProjection,
    V3SemanticBundle,
)


def build_v3_semantic_bundle(
    *,
    run_row: dict[str, object],
    run_summary: dict[str, object],
    battle_rows: list[dict[str, object]],
    battle_artifacts: dict[str, dict[str, object]],
) -> V3SemanticBundle:
    player_account_id = run_row.get("player_account_id")
    run_id = run_row.get("run_id")
    status = run_summary.get("status")
    ended_at_utc = run_summary.get("ended_at_utc")

    if not isinstance(run_id, str):
        raise ValueError("run_id is required")
    if not isinstance(player_account_id, str):
        raise ValueError("player_account_id is required")
    if not isinstance(status, str):
        raise ValueError("status is required")
    if not isinstance(ended_at_utc, str):
        raise ValueError("ended_at_utc is required")
    hero_id_value = run_row.get("hero_id")
    hero_id = hero_id_value if isinstance(hero_id_value, str) else None

    projections: list[BattleProjection] = []
    components: dict[str, ParsedBattleComponents] = {}

    for battle_row in battle_rows:
        battle_id = battle_row.get("battle_id")
        recorded_at_utc = battle_row.get("recorded_at_utc")
        battle_run_id = battle_row.get("run_id")
        if not isinstance(battle_id, str) or not isinstance(recorded_at_utc, str) or battle_run_id != run_id:
            continue

        projections.append(
            BattleProjection(
                battle_id=battle_id,
                run_id=run_id,
                recorded_at_utc=recorded_at_utc,
            )
        )
        artifact = battle_artifacts.get(battle_id, {})
        components[battle_id] = parse_battle_components(artifact)

    return V3SemanticBundle(
        run_projection=RunProjection(
            run_id=run_id,
            player_account_id=player_account_id,
            status=status,
            hero_id=hero_id,
            ended_at_utc=ended_at_utc,
        ),
        battle_projections=projections,
        battle_components=components,
    )
