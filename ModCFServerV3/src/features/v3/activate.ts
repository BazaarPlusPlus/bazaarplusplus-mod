import type { Env } from "../../env";
import { hashPassword } from "../../crypto/password";
import { json, readJson } from "../../http/json";
import { trimString } from "../../http/request";
import type { ActivateRequest } from "../../types/api";

export async function handleActivate(
  request: Request,
  env: Env,
): Promise<Response> {
  const body = (await readJson(request)) as ActivateRequest;
  const playerAccountId = trimString(body.player_account_id);
  const playerUsername = trimString(body.player_username);
  const password = trimString(body.password);
  const installationPublicKey = trimString(body.installation_public_key);

  if (!playerAccountId || !playerUsername || !password || !installationPublicKey) {
    return json({ error: "invalid_activate_request" }, { status: 400 });
  }

  const existingUser = await env.DB.prepare(
    `SELECT player_account_id FROM users WHERE player_account_id = ?`,
  )
    .bind(playerAccountId)
    .first<{ player_account_id: string }>();
  if (existingUser) {
    return json({ error: "player_account_id_claimed" }, { status: 409 });
  }

  const existingUsername = await env.DB.prepare(
    `SELECT player_account_id FROM users WHERE player_username = ?`,
  )
    .bind(playerUsername)
    .first<{ player_account_id: string }>();
  if (existingUsername) {
    return json({ error: "player_username_claimed" }, { status: 409 });
  }

  const nowUtc = new Date().toISOString();
  const installationId = `inst_${crypto.randomUUID().replace(/-/g, "")}`;
  const passwordHash = await hashPassword(password);

  await env.DB.batch([
    env.DB.prepare(
      `
        INSERT INTO users (
          player_account_id,
          player_username,
          password_hash,
          stream_platform,
          stream_channel_id,
          stream_url,
          created_at_utc,
          updated_at_utc,
          last_login_at_utc
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
      `,
    ).bind(
      playerAccountId,
      playerUsername,
      passwordHash,
      null,
      null,
      null,
      nowUtc,
      nowUtc,
      null,
    ),
    env.DB.prepare(
      `
        INSERT INTO installations (
          installation_id,
          player_account_id,
          public_key,
          status,
          created_at_utc,
          last_seen_at_utc,
          revoked_at_utc
        ) VALUES (?, ?, ?, ?, ?, ?, ?)
      `,
    ).bind(
      installationId,
      playerAccountId,
      installationPublicKey,
      "active",
      nowUtc,
      null,
      null,
    ),
  ]);

  return json({
    installation_id: installationId,
    status: "active",
  });
}
