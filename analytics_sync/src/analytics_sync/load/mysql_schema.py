from __future__ import annotations


TABLE_DDLS = [
    """
    CREATE TABLE runs (
      run_id VARCHAR(64) PRIMARY KEY,
      installation_id VARCHAR(64) NULL,
      player_account_id VARCHAR(64) NOT NULL,
      plugin_version VARCHAR(32) NULL,
      game_version VARCHAR(32) NULL,
      submitted_at_utc DATETIME(6) NULL,
      status VARCHAR(32) NOT NULL,
      hero_id VARCHAR(64) NULL,
      hero_name VARCHAR(64) NULL,
      player_rank VARCHAR(32) NULL,
      player_rating INT NULL,
      player_position INT NULL,
      started_at_utc DATETIME(6) NULL,
      ended_at_utc DATETIME(6) NOT NULL,
      final_day INT NULL,
      final_wins INT NULL,
      final_losses INT NULL,
      final_player_rank VARCHAR(32) NULL,
      final_player_rating INT NULL,
      final_player_position INT NULL,
      created_at DATETIME(6) NOT NULL,
      updated_at DATETIME(6) NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE battles (
      battle_id VARCHAR(64) PRIMARY KEY,
      run_id VARCHAR(64) NOT NULL,
      recorded_at_utc DATETIME(6) NOT NULL,
      day INT NULL,
      player_name VARCHAR(64) NULL,
      player_account_id VARCHAR(64) NULL,
      player_hero VARCHAR(64) NULL,
      player_rank VARCHAR(32) NULL,
      player_rating INT NULL,
      player_level INT NULL,
      opponent_name VARCHAR(64) NULL,
      opponent_account_id VARCHAR(64) NULL,
      opponent_hero VARCHAR(64) NULL,
      opponent_rank VARCHAR(32) NULL,
      opponent_rating INT NULL,
      opponent_level INT NULL,
      result VARCHAR(32) NULL,
      created_at DATETIME(6) NOT NULL,
      updated_at DATETIME(6) NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE card_templates (
      id BIGINT UNSIGNED PRIMARY KEY AUTO_INCREMENT,
      template_id VARCHAR(64) NOT NULL UNIQUE,
      created_at DATETIME(6) NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE skill_templates (
      id BIGINT UNSIGNED PRIMARY KEY AUTO_INCREMENT,
      template_id VARCHAR(64) NOT NULL UNIQUE,
      created_at DATETIME(6) NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE battle_cards (
      id BIGINT UNSIGNED PRIMARY KEY AUTO_INCREMENT,
      battle_id VARCHAR(64) NOT NULL,
      side VARCHAR(16) NOT NULL,
      slot_index INT NOT NULL,
      card_template_id BIGINT UNSIGNED NOT NULL,
      card_tier INT NULL,
      enchant_code VARCHAR(64) NULL,
      created_at DATETIME(6) NOT NULL,
      UNIQUE KEY uk_battle_cards_slot (battle_id, side, slot_index)
    )
    """.strip(),
    """
    CREATE TABLE battle_skills (
      id BIGINT UNSIGNED PRIMARY KEY AUTO_INCREMENT,
      battle_id VARCHAR(64) NOT NULL,
      side VARCHAR(16) NOT NULL,
      slot_index INT NOT NULL,
      skill_template_id BIGINT UNSIGNED NOT NULL,
      skill_tier INT NULL,
      created_at DATETIME(6) NOT NULL,
      UNIQUE KEY uk_battle_skills_slot (battle_id, side, slot_index)
    )
    """.strip(),
    """
    CREATE TABLE battle_slot_temperatures (
      id BIGINT UNSIGNED PRIMARY KEY AUTO_INCREMENT,
      battle_id VARCHAR(64) NOT NULL,
      side VARCHAR(16) NOT NULL,
      slot_index INT NOT NULL,
      temperature_state VARCHAR(16) NOT NULL,
      created_at DATETIME(6) NOT NULL,
      UNIQUE KEY uk_battle_slot_temperatures_slot (battle_id, side, slot_index)
    )
    """.strip(),
    """
    CREATE TABLE sync_checkpoints (
      source_name VARCHAR(64) PRIMARY KEY,
      cursor_updated_at DATETIME(6) NOT NULL,
      cursor_entity_id VARCHAR(64) NOT NULL,
      updated_at DATETIME(6) NOT NULL
    )
    """.strip(),
    """
    CREATE TABLE sync_run_tasks (
      id BIGINT UNSIGNED PRIMARY KEY AUTO_INCREMENT,
      task_type VARCHAR(32) NOT NULL,
      run_id VARCHAR(64) NOT NULL,
      status VARCHAR(32) NOT NULL,
      attempt_count INT NOT NULL,
      next_run_at DATETIME(6) NOT NULL,
      last_error TEXT NULL,
      created_at DATETIME(6) NOT NULL,
      updated_at DATETIME(6) NOT NULL,
      UNIQUE KEY uk_sync_run_tasks_task_type_run_id (task_type, run_id)
    )
    """.strip(),
]
