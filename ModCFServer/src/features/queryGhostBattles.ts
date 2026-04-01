import type { Env } from "../env";
import { json } from "../http/json";
import { getPlayerLink } from "../persistence/playerLinks";
import { requireVerifiedClient } from "./verifiedClient";

type GhostBattleRow = {
  battle_id: string;
  recorded_at_utc: string;
  day: number | null;
  hour: number | null;
  player_name: string | null;
  player_account_id: string | null;
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
  combat_kind: string;
  result: string | null;
  winner_combatant_id: string | null;
  loser_combatant_id: string | null;
  replay_object_key: string | null;
};

function parseClampedInt(
  value: string | null,
  fallback: number,
  min: number,
  max: number,
): number {
  const parsed = Number.parseInt(value ?? "", 10);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.min(max, Math.max(min, parsed));
}

export async function handleQueryGhostBattles(
  request: Request,
  env: Env,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env);
  if (verified instanceof Response) {
    return verified;
  }

  const playerLink = await getPlayerLink(env, verified.client.client_id);
  if (!playerLink) {
    return json({ error: "player_link_required" }, { status: 403 });
  }

  const url = new URL(request.url);
  const days = parseClampedInt(url.searchParams.get("days"), 3, 1, 14);
  const limit = parseClampedInt(url.searchParams.get("limit"), 200, 1, 200);
  const fromUtc = new Date(
    Date.now() - days * 24 * 60 * 60 * 1000,
  ).toISOString();

  const result = await env.DB.prepare(
    `
      SELECT
        b.battle_id,
        b.recorded_at_utc,
        b.day,
        b.hour,
        b.player_name,
        b.player_account_id,
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
        b.combat_kind,
        b.result,
        b.winner_combatant_id,
        b.loser_combatant_id,
        b.replay_object_key
      FROM battles AS b
      WHERE b.opponent_account_id = ?
        AND b.combat_kind = 'PVPCombat'
        AND b.recorded_at_utc >= ?
      ORDER BY b.recorded_at_utc DESC, b.battle_id DESC
      LIMIT ?
    `,
  )
    .bind(playerLink.player_account_id, fromUtc, limit)
    .all<GhostBattleRow>();

  return json({
    battles: result.results.map((row) => ({
      battle_id: row.battle_id,
      recorded_at_utc: row.recorded_at_utc,
      day: row.day,
      hour: row.hour,
      player_name: row.player_name,
      player_account_id: row.player_account_id,
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
      combat_kind: row.combat_kind,
      result: row.result,
      winner_combatant_id: row.winner_combatant_id,
      loser_combatant_id: row.loser_combatant_id,
      replay: {
        available: Boolean(row.replay_object_key),
      },
    })),
  });
}
