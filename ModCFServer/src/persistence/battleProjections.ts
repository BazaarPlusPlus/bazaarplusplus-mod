import type { Env } from "../env";
import type { ProjectedBattleQueryRow } from "../types/db";

export type BattleProjectionRecord = {
  battleId: string;
  runId: string;
  sourceClientId: string;
  recordedAtUtc: string;
  day: number | null;
  hour: number | null;
  encounterId: string | null;
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
  payloadJson: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

const D1_BATCH_LIMIT = 400;

export async function batchExecute(
  env: Env,
  statements: D1PreparedStatement[],
): Promise<void> {
  for (let i = 0; i < statements.length; i += D1_BATCH_LIMIT) {
    await env.DB.batch(statements.slice(i, i + D1_BATCH_LIMIT));
  }
}

function buildBattleUpsert(
  env: Env,
  battle: BattleProjectionRecord,
): D1PreparedStatement {
  return env.DB.prepare(
    `
      INSERT INTO pvp_battles (
        battle_id,
        run_id,
        source_client_id,
        recorded_at_utc,
        day,
        hour,
        encounter_id,
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
        payload_json,
        created_at_utc,
        updated_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(battle_id) DO UPDATE SET
        run_id = excluded.run_id,
        source_client_id = excluded.source_client_id,
        recorded_at_utc = excluded.recorded_at_utc,
        day = excluded.day,
        hour = excluded.hour,
        encounter_id = excluded.encounter_id,
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
        payload_json = excluded.payload_json,
        updated_at_utc = excluded.updated_at_utc
    `,
  ).bind(
    battle.battleId,
    battle.runId,
    battle.sourceClientId,
    battle.recordedAtUtc,
    battle.day,
    battle.hour,
    battle.encounterId,
    battle.playerName,
    battle.playerAccountId,
    battle.playerHero,
    battle.playerRank,
    battle.playerRating,
    battle.playerLevel,
    battle.opponentName,
    battle.opponentAccountId,
    battle.opponentHero,
    battle.opponentRank,
    battle.opponentRating,
    battle.opponentLevel,
    battle.combatKind,
    battle.result,
    battle.winnerCombatantId,
    battle.loserCombatantId,
    battle.payloadJson,
    battle.createdAtUtc,
    battle.updatedAtUtc,
  );
}

export async function replaceProjectedBattlesForRun(
  env: Env,
  input: {
    sourceClientId: string;
    runId: string;
    battles: BattleProjectionRecord[];
  },
): Promise<void> {
  const deleteStmt = env.DB.prepare(
    `
      DELETE FROM pvp_battles
      WHERE source_client_id = ?
        AND run_id = ?
    `,
  ).bind(input.sourceClientId, input.runId);

  const firstBatch = input.battles
    .slice(0, D1_BATCH_LIMIT - 1)
    .map((b) => buildBattleUpsert(env, b));
  await env.DB.batch([deleteStmt, ...firstBatch]);

  const remaining = input.battles
    .slice(D1_BATCH_LIMIT - 1)
    .map((b) => buildBattleUpsert(env, b));
  if (remaining.length > 0) {
    await batchExecute(env, remaining);
  }
}

export async function listProjectedBattlesAgainstAccountIds(
  env: Env,
  input: {
    opponentAccountIds: string[];
    fromUtc: string;
    limit: number;
  },
): Promise<ProjectedBattleQueryRow[]> {
  if (input.opponentAccountIds.length === 0) {
    return [];
  }

  const placeholders = input.opponentAccountIds.map(() => "?").join(", ");
  const result = await env.DB.prepare(
    `
      SELECT
        pb.battle_id,
        pb.recorded_at_utc,
        pb.opponent_account_id,
        pb.payload_json,
        CASE WHEN ru.battle_id IS NULL THEN 0 ELSE 1 END AS replay_available
      FROM pvp_battles AS pb
      LEFT JOIN replay_uploads AS ru
        ON ru.battle_id = pb.battle_id
      WHERE pb.opponent_account_id IN (${placeholders})
        AND pb.combat_kind = 'PVPCombat'
        AND pb.recorded_at_utc >= ?
      ORDER BY pb.recorded_at_utc DESC, pb.battle_id DESC
      LIMIT ?
    `,
  )
    .bind(...input.opponentAccountIds, input.fromUtc, input.limit)
    .all<ProjectedBattleQueryRow>();

  return result.results ?? [];
}

export async function getProjectedBattleById(
  env: Env,
  battleId: string,
): Promise<ProjectedBattleQueryRow | null> {
  return env.DB.prepare(
    `
      SELECT
        pb.battle_id,
        pb.recorded_at_utc,
        pb.opponent_account_id,
        pb.payload_json,
        CASE WHEN ru.battle_id IS NULL THEN 0 ELSE 1 END AS replay_available
      FROM pvp_battles AS pb
      LEFT JOIN replay_uploads AS ru
        ON ru.battle_id = pb.battle_id
      WHERE pb.battle_id = ?
      LIMIT 1
    `,
  )
    .bind(battleId)
    .first<ProjectedBattleQueryRow>();
}
