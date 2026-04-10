import type { Env } from "../../env";
import { verifyPassword } from "../../crypto/password";
import { json, readJson } from "../../http/json";
import { trimString } from "../../http/request";
import type { LoginRequest } from "../../types/api";

export async function handleLogin(
  request: Request,
  env: Env,
): Promise<Response> {
  const body = (await readJson(request)) as LoginRequest;
  const playerUsername = trimString(body.player_username);
  const password = trimString(body.password);

  if (!playerUsername || !password) {
    return json({ error: "invalid_login_request" }, { status: 400 });
  }

  const user = await env.DB.prepare(
    `
      SELECT
        player_account_id,
        player_username,
        password_hash
      FROM users
      WHERE player_username = ?
    `,
  )
    .bind(playerUsername)
    .first<{
      player_account_id: string;
      player_username: string;
      password_hash: string;
    }>();

  if (!user || !(await verifyPassword(password, user.password_hash))) {
    return json({ error: "invalid_credentials" }, { status: 401 });
  }

  const nowUtc = new Date().toISOString();
  const expiresAtUtc = new Date(Date.now() + 30 * 60 * 1000).toISOString();
  const sessionToken = `sess_${crypto.randomUUID().replace(/-/g, "")}`;

  await env.DB.batch([
    env.DB.prepare(
      `
        INSERT INTO installation_sessions (
          session_id,
          player_account_id,
          created_at_utc,
          expires_at_utc,
          revoked_at_utc
        ) VALUES (?, ?, ?, ?, ?)
      `,
    ).bind(sessionToken, user.player_account_id, nowUtc, expiresAtUtc, null),
    env.DB.prepare(
      `
        UPDATE users
        SET
          last_login_at_utc = ?,
          updated_at_utc = ?
        WHERE player_account_id = ?
      `,
    ).bind(nowUtc, nowUtc, user.player_account_id),
  ]);

  return json({
    session_token: sessionToken,
    player_account_id: user.player_account_id,
    expires_at_utc: expiresAtUtc,
  });
}

