import type { Env } from "../env";
import { json } from "../http/json";
import { getPlayerLink } from "../persistence/playerLinks";
import { createReplayToken } from "../persistence/replayTokens";
import { requireVerifiedClient } from "./verifiedClient";

export async function handleCreateReplayLink(
  request: Request,
  env: Env,
  battleId: string,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env);
  if (verified instanceof Response) {
    return verified;
  }

  const playerLink = await getPlayerLink(env, verified.client.client_id);
  if (!playerLink) {
    return json({ error: "player_link_required" }, { status: 403 });
  }

  const battle = await env.DB.prepare(
    `
      SELECT *
      FROM battles
      WHERE battle_id = ?
    `,
  )
    .bind(battleId)
    .first<{
      opponent_account_id: string | null;
    }>();
  if (!battle) {
    return json({ error: "battle_not_found" }, { status: 404 });
  }
  if (battle.opponent_account_id !== playerLink.player_account_id) {
    return json({ error: "battle_forbidden" }, { status: 403 });
  }

  const createdAtUtc = new Date().toISOString();
  const expiresAtUtc = new Date(Date.now() + 5 * 60 * 1000).toISOString();
  const token = await createReplayToken(env, {
    battleId,
    requestedByPlayerAccountId: playerLink.player_account_id,
    expiresAtUtc,
    createdAtUtc,
  });

  return json({
    download_url: new URL(`/replays/${token}`, request.url).toString(),
  });
}
