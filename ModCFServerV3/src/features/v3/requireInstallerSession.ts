import type { Env } from "../../env";

export type InstallerSessionContext = {
  sessionId: string;
  playerAccountId: string;
};

export async function requireInstallerSession(
  request: Request,
  env: Env,
): Promise<InstallerSessionContext | null> {
  const authorization = request.headers.get("authorization") ?? "";
  const match = authorization.match(/^Bearer\s+(.+)$/i);
  const sessionId = match?.[1]?.trim() ?? "";
  if (!sessionId) {
    return null;
  }

  const row = await env.DB.prepare(
    `
      SELECT
        session_id,
        player_account_id,
        expires_at_utc,
        revoked_at_utc
      FROM installation_sessions
      WHERE session_id = ?
    `,
  )
    .bind(sessionId)
    .first<{
      session_id: string;
      player_account_id: string;
      expires_at_utc: string;
      revoked_at_utc: string | null;
    }>();

  if (!row || row.revoked_at_utc != null || row.expires_at_utc <= new Date().toISOString()) {
    return null;
  }

  return {
    sessionId: row.session_id,
    playerAccountId: row.player_account_id,
  };
}

