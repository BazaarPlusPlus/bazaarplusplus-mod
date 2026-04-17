import type { Env } from "../../env";
import { json, readJson } from "../../http/json";
import { hashPassword } from "../../crypto/password";
import { generateBearerToken } from "../../token/generate";

type ActivateRequest = {
  player_account_id?: unknown;
  player_username?: unknown;
  password?: unknown;
};

export async function handleActivate(request: Request, env: Env): Promise<Response> {
  const body = (await readJson(request)) as ActivateRequest;
  const playerAccountId = typeof body.player_account_id === "string" ? body.player_account_id.trim() : "";
  const playerUsername = typeof body.player_username === "string" ? body.player_username.trim() : "";
  const password = typeof body.password === "string" ? body.password : "";

  if (!playerAccountId || !playerUsername || !password) {
    return json({ error: "invalid_request" }, { status: 400 });
  }

  const passwordHash = await hashPassword(password);
  const token = generateBearerToken();
  const nowUtc = new Date().toISOString();

  try {
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO users (player_account_id, player_username, password_hash, created_at_utc, updated_at_utc, last_login_at_utc)
         VALUES (?, ?, ?, ?, ?, ?)`,
      ).bind(playerAccountId, playerUsername, passwordHash, nowUtc, nowUtc, nowUtc),
      env.DB.prepare(
        `INSERT INTO tokens (token, player_account_id, issued_at_utc) VALUES (?, ?, ?)`,
      ).bind(token, playerAccountId, nowUtc),
    ]);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (/UNIQUE constraint failed:\s*users\.player_account_id/i.test(message)) {
      return json({ error: "player_account_id_taken" }, { status: 409 });
    }
    if (/UNIQUE constraint failed:\s*users\.player_username/i.test(message)) {
      return json({ error: "player_username_taken" }, { status: 409 });
    }
    throw error;
  }

  return json({
    token,
    player_account_id: playerAccountId,
    player_username: playerUsername,
  });
}
