import type { Env } from "../env";

export async function upsertBattle(
  env: Env,
  input: {
    battleId: string;
    runId: string | null;
    clientId: string;
    uploaderPlayerAccountId: string | null;
    recordedAtUtc: string;
    day: number | null;
    hour: number | null;
    playerName: string | null;
    playerAccountId: string | null;
    playerHero: string | null;
    playerRank: string | null;
    playerRating: number | null;
    playerLevel: number | null;
    opponentName: string | null;
    opponentAccountId: string | null;
    opponentHero: string | null;
    opponentRank: string | null;
    opponentRating: number | null;
    opponentLevel: number | null;
    combatKind: string;
    result: string | null;
    winnerCombatantId: string | null;
    loserCombatantId: string | null;
    replaySchemaVersion: number;
    replayObjectKey: string;
    replaySizeBytes: number;
    createdAtUtc: string;
    updatedAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO battles (
        battle_id,
        run_id,
        client_id,
        uploader_player_account_id,
        recorded_at_utc,
        day,
        hour,
        player_name,
        player_account_id,
        player_hero,
        player_rank,
        player_rating,
        player_level,
        opponent_name,
        opponent_account_id,
        opponent_hero,
        opponent_rank,
        opponent_rating,
        opponent_level,
        combat_kind,
        result,
        winner_combatant_id,
        loser_combatant_id,
        replay_schema_version,
        replay_object_key,
        replay_size_bytes,
        created_at_utc,
        updated_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(battle_id) DO UPDATE SET
        run_id = excluded.run_id,
        client_id = excluded.client_id,
        uploader_player_account_id = excluded.uploader_player_account_id,
        recorded_at_utc = excluded.recorded_at_utc,
        day = excluded.day,
        hour = excluded.hour,
        player_name = excluded.player_name,
        player_account_id = excluded.player_account_id,
        player_hero = excluded.player_hero,
        player_rank = excluded.player_rank,
        player_rating = excluded.player_rating,
        player_level = excluded.player_level,
        opponent_name = excluded.opponent_name,
        opponent_account_id = excluded.opponent_account_id,
        opponent_hero = excluded.opponent_hero,
        opponent_rank = excluded.opponent_rank,
        opponent_rating = excluded.opponent_rating,
        opponent_level = excluded.opponent_level,
        combat_kind = excluded.combat_kind,
        result = excluded.result,
        winner_combatant_id = excluded.winner_combatant_id,
        loser_combatant_id = excluded.loser_combatant_id,
        replay_schema_version = excluded.replay_schema_version,
        replay_object_key = excluded.replay_object_key,
        replay_size_bytes = excluded.replay_size_bytes,
        updated_at_utc = excluded.updated_at_utc
      WHERE
        battles.run_id IS NOT excluded.run_id OR
        battles.client_id IS NOT excluded.client_id OR
        battles.uploader_player_account_id IS NOT excluded.uploader_player_account_id OR
        battles.recorded_at_utc IS NOT excluded.recorded_at_utc OR
        battles.day IS NOT excluded.day OR
        battles.hour IS NOT excluded.hour OR
        battles.player_name IS NOT excluded.player_name OR
        battles.player_account_id IS NOT excluded.player_account_id OR
        battles.player_hero IS NOT excluded.player_hero OR
        battles.player_rank IS NOT excluded.player_rank OR
        battles.player_rating IS NOT excluded.player_rating OR
        battles.player_level IS NOT excluded.player_level OR
        battles.opponent_name IS NOT excluded.opponent_name OR
        battles.opponent_account_id IS NOT excluded.opponent_account_id OR
        battles.opponent_hero IS NOT excluded.opponent_hero OR
        battles.opponent_rank IS NOT excluded.opponent_rank OR
        battles.opponent_rating IS NOT excluded.opponent_rating OR
        battles.opponent_level IS NOT excluded.opponent_level OR
        battles.combat_kind IS NOT excluded.combat_kind OR
        battles.result IS NOT excluded.result OR
        battles.winner_combatant_id IS NOT excluded.winner_combatant_id OR
        battles.loser_combatant_id IS NOT excluded.loser_combatant_id OR
        battles.replay_schema_version IS NOT excluded.replay_schema_version OR
        battles.replay_object_key IS NOT excluded.replay_object_key OR
        battles.replay_size_bytes IS NOT excluded.replay_size_bytes
    `,
  )
    .bind(
      input.battleId,
      input.runId,
      input.clientId,
      input.uploaderPlayerAccountId,
      input.recordedAtUtc,
      input.day,
      input.hour,
      input.playerName,
      input.playerAccountId,
      input.playerHero,
      input.playerRank,
      input.playerRating,
      input.playerLevel,
      input.opponentName,
      input.opponentAccountId,
      input.opponentHero,
      input.opponentRank,
      input.opponentRating,
      input.opponentLevel,
      input.combatKind,
      input.result,
      input.winnerCombatantId,
      input.loserCombatantId,
      input.replaySchemaVersion,
      input.replayObjectKey,
      input.replaySizeBytes,
      input.createdAtUtc,
      input.updatedAtUtc,
    )
    .run();
}
