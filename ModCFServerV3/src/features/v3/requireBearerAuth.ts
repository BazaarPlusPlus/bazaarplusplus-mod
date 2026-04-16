import type { Env } from "../../env";
import { json } from "../../http/json";

export type BearerAuth = {
  token: string;
  playerAccountId: string;
};

type TokenRow = {
  token: string;
  player_account_id: string;
  revoked_at_utc: string | null;
};

export async function requireBearerAuth(
  request: Request,
  env: Env,
): Promise<BearerAuth | Response> {
  const header = request.headers.get("Authorization") ?? "";
  const match = header.match(/^Bearer\s+(\S+)$/);
  if (!match) {
    return json({ error: "invalid_token" }, { status: 401 });
  }
  const token = match[1]!;

  const row = await env.DB.prepare(
    `SELECT token, player_account_id, revoked_at_utc
     FROM tokens
     WHERE token = ?`,
  )
    .bind(token)
    .first<TokenRow>();

  if (!row || row.revoked_at_utc != null) {
    return json({ error: "invalid_token" }, { status: 401 });
  }

  await env.DB.prepare(
    `UPDATE tokens SET last_used_at_utc = ? WHERE token = ?`,
  )
    .bind(new Date().toISOString(), token)
    .run()
    .catch(() => undefined);

  return { token: row.token, playerAccountId: row.player_account_id };
}
