from analytics_sync.compat.run_bundle_builder import build_run_bundle_upload_request_v2


def test_build_run_bundle_upload_request_v2_preserves_null_for_missing_fields():
    bundle = build_run_bundle_upload_request_v2(
        run_row={"run_id": "run-1", "player_account_id": "player-123", "hero_id": None},
        run_summary={"status": "completed", "ended_at_utc": "2026-04-09T00:00:00Z"},
        battle_rows=[],
        battle_artifacts={},
    )

    assert bundle.run_projection["run_id"] == "run-1"
    assert bundle.run_projection["hero_id"] is None
    assert bundle.battle_projections == []
