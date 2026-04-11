import { allowUnauthenticatedReplayLinks } from "../../config/v3";
import type { Env } from "../../env";
import { json } from "../../http/json";
import { requireInstallationAuth } from "./requireInstallationAuth";

export async function handleCreateReplayLink(
  request: Request,
  env: Env,
  battleId: string,
): Promise<Response> {
  let requesterPlayerAccountId: string | null = null;
  if (!allowUnauthenticatedReplayLinks(env)) {
    const auth = await requireInstallationAuth(request, env);
    if (auth instanceof Response) {
      return auth;
    }
    if (auth == null) {
      return json({ error: "installation_auth_required" }, { status: 401 });
    }

    requesterPlayerAccountId = auth.playerAccountId;
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
  if (
    requesterPlayerAccountId != null &&
    battle.opponent_account_id !== requesterPlayerAccountId
  ) {
    return json({ error: "battle_forbidden" }, { status: 403 });
  }

  const tokenOwnerPlayerAccountId = requesterPlayerAccountId ?? battle.opponent_account_id;
  if (!tokenOwnerPlayerAccountId) {
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
      tokenOwnerPlayerAccountId,
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

