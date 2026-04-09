from __future__ import annotations


def build_runs_query() -> str:
    return """
    SELECT run_id, player_account_id, status, hero_id, hero_name,
           started_at_utc, ended_at_utc, final_day, final_wins, final_losses,
           summary_schema_version, summary_object_key, created_at_utc, updated_at_utc
    FROM runs
    WHERE (updated_at_utc > ?)
       OR (updated_at_utc = ? AND run_id > ?)
    ORDER BY updated_at_utc, run_id
    LIMIT ?
    """.strip()


def build_battles_query() -> str:
    return """
    SELECT battle_id, run_id, recorded_at_utc, day,
           player_name, player_account_id, player_hero, player_rank, player_rating, player_level,
           opponent_name, opponent_account_id, opponent_hero, opponent_rank, opponent_rating, opponent_level,
           result, replay_object_key, created_at_utc, updated_at_utc
    FROM battles
    WHERE (updated_at_utc > ?)
       OR (updated_at_utc = ? AND battle_id > ?)
    ORDER BY updated_at_utc, battle_id
    LIMIT ?
    """.strip()


def build_battles_for_run_ids_query(run_count: int) -> str:
    if run_count <= 0:
        raise ValueError("run_count must be positive")

    placeholders = ",".join("?" for _ in range(run_count))
    return f"""
    SELECT battle_id, run_id, recorded_at_utc, day,
           player_name, player_account_id, player_hero, player_rank, player_rating, player_level,
           opponent_name, opponent_account_id, opponent_hero, opponent_rank, opponent_rating, opponent_level,
           result, replay_object_key, created_at_utc, updated_at_utc
    FROM battles
    WHERE run_id IN ({placeholders})
    ORDER BY run_id, recorded_at_utc, battle_id
    """.strip()
