import type { Env } from "../../env";
import { getGhostQueryLookbackDays } from "../../config/v3";
import { json, jsonError } from "../../http/json";
import { parseClampedInteger, trimString } from "../../http/request";

type GhostBattleRow = {
  battle_id: string;
  recorded_at_utc: string;
  day: number | null;
  player_name: string | null;
  player_account_id_in_payload: string | null;
  player_hero: string | null;
  player_rank: string | null;
  player_rating: number | null;
  player_level: number | null;
  opponent_name: string | null;
  opponent_account_id: string | null;
  opponent_hero: string | null;
  opponent_rank: string | null;
  opponent_rating: number | null;
  opponent_level: number | null;
  result: string | null;
  replay_available: number;
  is_bundle_final_battle: number;
};

export async function handleQueryGhostBattles(
  request: Request,
  env: Env,
): Promise<Response> {
  const url = new URL(request.url);
  const playerAccountId = trimString(url.searchParams.get("player_account_id"));
  if (!playerAccountId) {
    return jsonError("invalid_request");
  }

  const lookbackDays = getGhostQueryLookbackDays(env);
  const limit = parseClampedInteger(url.searchParams.get("limit"), 200, 1, 200);
  const fromUtc = new Date(Date.now() - lookbackDays * 24 * 60 * 60 * 1000).toISOString();

  // Scan shape: opponent_account_id has a covering index
  // (idx_battles_opponent_recorded_covering from migration 0008). Query is
  // index-seek + bounded-range scan on recorded_at_utc per opponent, capped at
  // LIMIT 200 — O(lookback-window-rows-for-one-opponent) read, not full scan.
  const result = await env.DB.prepare(
    `
      SELECT
        b.battle_id,
        b.recorded_at_utc,
        b.day,
        b.player_name,
        b.player_account_id_in_payload,
        b.player_hero,
        b.player_rank,
        b.player_rating,
        b.player_level,
        b.opponent_name,
        b.opponent_account_id,
        b.opponent_hero,
        b.opponent_rank,
        b.opponent_rating,
        b.opponent_level,
        b.result,
        b.replay_available,
        b.is_bundle_final_battle
      FROM battles AS b
      WHERE b.opponent_account_id = ?
        AND b.recorded_at_utc >= ?
      ORDER BY b.recorded_at_utc DESC, b.battle_id DESC
      LIMIT ?
    `,
  )
    .bind(playerAccountId, fromUtc, limit)
    .all<GhostBattleRow>();

  return json({
    battles: result.results.map((row) => ({
      battle_id: row.battle_id,
      recorded_at_utc: row.recorded_at_utc,
      day: row.day,
      player_name: row.player_name,
      player_account_id: row.player_account_id_in_payload,
      player_hero: row.player_hero,
      player_rank: row.player_rank,
      player_rating: row.player_rating,
      player_level: row.player_level,
      opponent_name: row.opponent_name,
      opponent_account_id: row.opponent_account_id,
      opponent_hero: row.opponent_hero,
      opponent_rank: row.opponent_rank,
      opponent_rating: row.opponent_rating,
      opponent_level: row.opponent_level,
      result: row.result,
      is_bundle_final_battle: row.is_bundle_final_battle === 1,
      replay: {
        available: row.replay_available === 1,
      },
    })),
  });
}
