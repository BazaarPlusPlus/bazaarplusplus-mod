from analytics_sync.transform.project_run import project_run_row


def test_project_run_row_requires_player_account_id():
    projected = project_run_row(
        {
            "run_id": "run-1",
            "player_account_id": "player-123",
            "status": "completed",
            "hero_id": None,
            "ended_at_utc": "2026-04-09T00:00:00Z",
        }
    )

    assert projected["run_id"] == "run-1"
    assert projected["player_account_id"] == "player-123"
