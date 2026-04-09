from __future__ import annotations


TABLE_DDLS = [
    """
    CREATE TABLE runs (
      run_id TEXT PRIMARY KEY,
      installation_id TEXT NULL,
      player_account_id TEXT NOT NULL,
      plugin_version TEXT NULL,
      game_version TEXT NULL,
      submitted_at_utc TEXT NULL,
      status TEXT NOT NULL,
      hero_id TEXT NULL,
      hero_name TEXT NULL,
      player_rank TEXT NULL,
      player_rating INTEGER NULL,
      player_position INTEGER NULL,
      started_at_utc TEXT NULL,
      ended_at_utc TEXT NOT NULL,
      final_day INTEGER NULL,
      final_wins INTEGER NULL,
      final_losses INTEGER NULL,
      final_player_rank TEXT NULL,
      final_player_rating INTEGER NULL,
      final_player_position INTEGER NULL,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE battles (
      battle_id TEXT PRIMARY KEY,
      run_id TEXT NOT NULL,
      recorded_at_utc TEXT NOT NULL,
      day INTEGER NULL,
      player_name TEXT NULL,
      player_account_id TEXT NULL,
      player_hero TEXT NULL,
      player_rank TEXT NULL,
      player_rating INTEGER NULL,
      player_level INTEGER NULL,
      opponent_name TEXT NULL,
      opponent_account_id TEXT NULL,
      opponent_hero TEXT NULL,
      opponent_rank TEXT NULL,
      opponent_rating INTEGER NULL,
      opponent_level INTEGER NULL,
      result TEXT NULL,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE card_templates (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      template_id TEXT NOT NULL UNIQUE,
      created_at TEXT NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE skill_templates (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      template_id TEXT NOT NULL UNIQUE,
      created_at TEXT NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE battle_cards (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      battle_id TEXT NOT NULL,
      side TEXT NOT NULL,
      slot_index INTEGER NOT NULL,
      card_template_id INTEGER NOT NULL,
      card_tier INTEGER NULL,
      enchant_code TEXT NULL,
      created_at TEXT NOT NULL,
      UNIQUE (battle_id, side, slot_index)
    )
    """.strip(),
    """
    CREATE TABLE battle_skills (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      battle_id TEXT NOT NULL,
      side TEXT NOT NULL,
      slot_index INTEGER NOT NULL,
      skill_template_id INTEGER NOT NULL,
      skill_tier INTEGER NULL,
      created_at TEXT NOT NULL,
      UNIQUE (battle_id, side, slot_index)
    )
    """.strip(),
    """
    CREATE TABLE battle_slot_temperatures (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      battle_id TEXT NOT NULL,
      side TEXT NOT NULL,
      slot_index INTEGER NOT NULL,
      temperature_state TEXT NOT NULL,
      created_at TEXT NOT NULL,
      UNIQUE (battle_id, side, slot_index)
    )
    """.strip(),
    """
    CREATE TABLE sync_checkpoints (
      source_name TEXT PRIMARY KEY,
      cursor_updated_at TEXT NOT NULL,
      cursor_entity_id TEXT NOT NULL,
      updated_at TEXT NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE sync_run_tasks (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      task_type TEXT NOT NULL,
      run_id TEXT NOT NULL,
      status TEXT NOT NULL,
      attempt_count INTEGER NOT NULL,
      next_run_at TEXT NOT NULL,
      last_error TEXT NULL,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL,
      UNIQUE (task_type, run_id)
    )
    """.strip(),
]
