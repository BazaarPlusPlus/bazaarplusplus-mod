from analytics_sync.compat.run_bundle_builder import build_v3_semantic_bundle


def test_build_v3_semantic_bundle_preserves_null_for_missing_fields():
    bundle = build_v3_semantic_bundle(
        run_row={"run_id": "run-1", "player_account_id": "player-123", "hero_id": None},
        run_summary={"status": "completed", "ended_at_utc": "2026-04-09T00:00:00Z"},
        battle_rows=[],
        battle_artifacts={},
    )

    assert bundle.run_projection.run_id == "run-1"
    assert bundle.run_projection.hero_id is None
    assert bundle.battle_projections == []
