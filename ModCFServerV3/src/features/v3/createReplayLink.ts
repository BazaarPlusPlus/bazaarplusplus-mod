import type { Env } from "../../env";
import { json } from "../../http/json";
import { requireInstallationAuth } from "./requireInstallationAuth";

export async function handleCreateReplayLink(
  request: Request,
  env: Env,
  battleId: string,
): Promise<Response> {
  const auth = await requireInstallationAuth(request, env);
  if (auth instanceof Response) {
    return auth;
  }

  const battle = await env.DB.prepare(
    `
      SELECT
        battle_id,
        opponent_account_id
      FROM battles
      WHERE battle_id = ?
    `,
  )
    .bind(battleId)
    .first<{
      battle_id: string;
      opponent_account_id: string | null;
    }>();
  if (!battle) {
    return json({ error: "battle_not_found" }, { status: 404 });
  }
  if (battle.opponent_account_id !== auth.playerAccountId) {
    return json({ error: "battle_forbidden" }, { status: 403 });
  }

  const createdAtUtc = new Date().toISOString();
  const expiresAtUtc = new Date(Date.now() + 5 * 60 * 1000).toISOString();
  const token = `replay_${crypto.randomUUID().replace(/-/g, "")}`;

  await env.DB.prepare(
    `
      INSERT INTO replay_tokens (
        token,
        battle_id,
        requested_by_player_account_id,
        expires_at_utc,
        created_at_utc,
        used_at_utc,
        revoked_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?)
    `,
  )
    .bind(
      token,
      battleId,
      auth.playerAccountId,
      expiresAtUtc,
      createdAtUtc,
      null,
      null,
    )
    .run();

  return json({
    download_url: new URL(`/replays/${token}`, request.url).toString(),
  });
}

