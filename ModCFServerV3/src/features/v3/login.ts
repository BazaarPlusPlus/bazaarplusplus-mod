import type { Env } from "../../env";
import { verifyPassword } from "../../crypto/password";
import { json, readJson } from "../../http/json";
import { generateBearerToken } from "../../token/generate";

type LoginRequest = {
  player_username?: unknown;
  password?: unknown;
};

type UserRow = {
  player_account_id: string;
  player_username: string;
  password_hash: string;
};

export async function handleLogin(
  request: Request,
  env: Env,
): Promise<Response> {
  const body = (await readJson(request)) as LoginRequest;
  const username = typeof body.player_username === "string" ? body.player_username.trim() : "";
  const password = typeof body.password === "string" ? body.password : "";

  if (!username || !password) {
    return json({ error: "invalid_request" }, { status: 400 });
  }

  const user = await env.DB.prepare(
    `SELECT player_account_id, player_username, password_hash
     FROM users
     WHERE player_username = ?`,
  )
    .bind(username)
    .first<UserRow>();

  if (!user || !(await verifyPassword(password, user.password_hash))) {
    return json({ error: "invalid_credentials" }, { status: 401 });
  }

  const token = generateBearerToken();
  const nowUtc = new Date().toISOString();

  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc) VALUES (?, ?, ?)`,
    ).bind(token, user.player_account_id, nowUtc),
    env.DB.prepare(
      `UPDATE users SET last_login_at_utc = ?, updated_at_utc = ? WHERE player_account_id = ?`,
    ).bind(nowUtc, nowUtc, user.player_account_id),
  ]);

  return json({
    token,
    player_account_id: user.player_account_id,
    player_username: user.player_username,
  });
}
