import type { Env } from "../../env";
import { getGhostQueryLookbackDays } from "../../config/v3";
import { json } from "../../http/json";
import { requireBearerAuth } from "./requireBearerAuth";

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

function parseClampedInt(
  value: string | null,
  fallback: number,
  min: number,
  max: number,
): number {
  if (!value) {
    return fallback;
  }

  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.min(max, Math.max(min, parsed));
}

export async function handleQueryGhostBattles(
  request: Request,
  env: Env,
): Promise<Response> {
  const auth = await requireBearerAuth(request, env);
  if (auth instanceof Response) {
    return auth;
  }

  const url = new URL(request.url);
  const lookbackDays = getGhostQueryLookbackDays(env);
  const limit = parseClampedInt(url.searchParams.get("limit"), 200, 1, 200);
  const fromUtc = new Date(Date.now() - lookbackDays * 24 * 60 * 60 * 1000).toISOString();

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
    .bind(auth.playerAccountId, fromUtc, limit)
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
