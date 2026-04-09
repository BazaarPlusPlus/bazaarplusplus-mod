from __future__ import annotations


def project_run_row(run_projection: dict[str, object]) -> dict[str, object]:
    run_id = run_projection.get("run_id")
    player_account_id = run_projection.get("player_account_id")
    status = run_projection.get("status")
    ended_at_utc = run_projection.get("ended_at_utc")

    if not isinstance(run_id, str):
        raise ValueError("run_id is required")
    if not isinstance(player_account_id, str):
        raise ValueError("player_account_id is required")
    if not isinstance(status, str):
        raise ValueError("status is required")
    if not isinstance(ended_at_utc, str):
        raise ValueError("ended_at_utc is required")

    return {
        "run_id": run_id,
        "player_account_id": player_account_id,
        "status": status,
        "hero_id": run_projection.get("hero_id") if isinstance(run_projection.get("hero_id"), str) else None,
        "ended_at_utc": ended_at_utc,
    }
