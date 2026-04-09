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
        "hero_name": run_projection.get("hero_name") if isinstance(run_projection.get("hero_name"), str) else None,
        "player_rank": (
            run_projection.get("player_rank") if isinstance(run_projection.get("player_rank"), str) else None
        ),
        "player_rating": (
            run_projection.get("player_rating") if isinstance(run_projection.get("player_rating"), int) else None
        ),
        "player_position": (
            run_projection.get("player_position") if isinstance(run_projection.get("player_position"), int) else None
        ),
        "ended_at_utc": ended_at_utc,
        "final_day": run_projection.get("final_day") if isinstance(run_projection.get("final_day"), int) else None,
        "final_wins": run_projection.get("final_wins") if isinstance(run_projection.get("final_wins"), int) else None,
        "final_losses": run_projection.get("final_losses") if isinstance(run_projection.get("final_losses"), int) else None,
        "final_player_rank": (
            run_projection.get("final_player_rank")
            if isinstance(run_projection.get("final_player_rank"), str)
            else None
        ),
        "final_player_rating": (
            run_projection.get("final_player_rating")
            if isinstance(run_projection.get("final_player_rating"), int)
            else None
        ),
        "final_player_position": (
            run_projection.get("final_player_position")
            if isinstance(run_projection.get("final_player_position"), int)
            else None
        ),
        "installation_id": (
            run_projection.get("installation_id") if isinstance(run_projection.get("installation_id"), str) else None
        ),
        "plugin_version": (
            run_projection.get("plugin_version") if isinstance(run_projection.get("plugin_version"), str) else None
        ),
        "submitted_at_utc": (
            run_projection.get("submitted_at_utc")
            if isinstance(run_projection.get("submitted_at_utc"), str)
            else None
        ),
    }
