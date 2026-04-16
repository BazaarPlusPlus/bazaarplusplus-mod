import type { Env } from "../../env";
import { json } from "../../http/json";

export type BearerAuth = {
  token: string;
  playerId: string;
  installationId: string;
};

type V3TokenRow = {
  token: string;
  player_id: string;
  installation_id: string;
  revoked_at_utc: string | null;
  last_used_at_utc: string | null;
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

  const token = match[1];
  const row = await env.DB.prepare(
    `SELECT * FROM tokens WHERE token = ?`,
  )
    .bind(token)
    .first<V3TokenRow>();

  if (!row || row.revoked_at_utc != null) {
    return json({ error: "invalid_token" }, { status: 401 });
  }

  const isoNow = new Date().toISOString();
  await env.DB.prepare(
    `UPDATE tokens SET last_used_at_utc = ? WHERE token = ?`,
  )
    .bind(isoNow, token)
    .run()
    .catch(() => undefined);

  return {
    token,
    playerId: row.player_id,
    installationId: row.installation_id,
  };
}
