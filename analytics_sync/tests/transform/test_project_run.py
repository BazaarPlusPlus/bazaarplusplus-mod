from analytics_sync.transform.project_run import project_run_row


def test_project_run_row_requires_player_account_id():
    projected = project_run_row(
        {
            "run_id": "run-1",
            "player_account_id": "player-123",
            "status": "completed",
            "hero_id": None,
            "hero_name": "Karnok",
            "player_rank": "Silver",
            "player_rating": 1200,
            "final_player_rank": "Gold",
            "final_player_rating": 1337,
            "ended_at_utc": "2026-04-09T00:00:00Z",
            "final_day": 8,
            "final_wins": 1,
            "final_losses": 7,
        }
    )

    assert projected["run_id"] == "run-1"
    assert projected["player_account_id"] == "player-123"
    assert projected["hero_name"] == "Karnok"
    assert projected["player_rank"] == "Silver"
    assert projected["player_rating"] == 1200
    assert projected["final_player_rank"] == "Gold"
    assert projected["final_player_rating"] == 1337
    assert projected["final_day"] == 8
    assert projected["final_wins"] == 1
    assert projected["final_losses"] == 7
