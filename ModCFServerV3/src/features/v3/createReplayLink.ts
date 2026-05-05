import type { Env } from "../../env";
import { json } from "../../http/json";

export async function handleCreateReplayLink(
  request: Request,
  env: Env,
  battleId: string,
): Promise<Response> {
  const battleRow = await env.DB.prepare(
    `
      SELECT
        battle_id,
        player_account_id,
        opponent_account_id
      FROM battles
      WHERE battle_id = ?
    `,
  )
    .bind(battleId)
    .first<{
      battle_id: string;
      player_account_id: string | null;
      opponent_account_id: string | null;
    }>();
  if (!battleRow) {
    return json({ error: "battle_not_found" }, { status: 404 });
  }

  const tokenOwnerPlayerAccountId = battleRow.opponent_account_id;
  if (!tokenOwnerPlayerAccountId) {
    return json({ error: "replay_forbidden" }, { status: 403 });
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
    expires_at_utc: expiresAtUtc,
  });
}
