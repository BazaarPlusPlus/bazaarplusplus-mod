from __future__ import annotations

from analytics_sync.compat.battle_artifact_parser import parse_battle_components
from analytics_sync.compat.v3_models import (
    ParsedBattleComponents,
    RunBundleUploadRequestV2,
)


def build_run_bundle_upload_request_v2(
    *,
    run_row: dict[str, object],
    run_summary: dict[str, object],
    battle_rows: list[dict[str, object]],
    battle_artifacts: dict[str, dict[str, object]],
) -> RunBundleUploadRequestV2:
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

    projections: list[dict[str, object]] = []
    components: dict[str, ParsedBattleComponents] = {}

    for battle_row in battle_rows:
        battle_id = battle_row.get("battle_id")
        recorded_at_utc = battle_row.get("recorded_at_utc")
        battle_run_id = battle_row.get("run_id")
        if not isinstance(battle_id, str) or not isinstance(recorded_at_utc, str) or battle_run_id != run_id:
            continue

        projections.append(dict(battle_row))
        artifact = battle_artifacts.get(battle_id, {})
        components[battle_id] = parse_battle_components(artifact)

    return RunBundleUploadRequestV2(
        run_projection={
            "run_id": run_id,
            "player_account_id": player_account_id,
            "status": status,
            "hero_id": hero_id,
            "ended_at_utc": ended_at_utc,
            "hero_name": run_summary.get("hero_name"),
            "final_day": run_summary.get("final_day"),
            "final_wins": run_summary.get("final_wins"),
            "final_losses": run_summary.get("final_losses"),
            "installation_id": run_summary.get("install_id"),
            "plugin_version": run_summary.get("plugin_version"),
            "submitted_at_utc": run_summary.get("submitted_at_utc"),
        },
        battle_projections=projections,
        battle_components=components,
    )
