import type { Env } from "../env";
import { json } from "../http/json";
import { getPlayerLink } from "../persistence/playerLinks";
import { requireVerifiedClient } from "./verifiedClient";

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

  const result = await env.DB.prepare(
    `
      SELECT *
      FROM battles AS b
      WHERE b.opponent_account_id = ?
      ORDER BY b.recorded_at_utc DESC, b.battle_id DESC
    `,
  )
    .bind(playerLink.player_account_id)
    .all();

  return json({
    battles: result.results,
  });
}
