import { bytesToBase64 } from "../crypto/base64";
import type { Env } from "../env";

export async function createReplayToken(
  env: Env,
  input: {
    battleId: string;
    requestedByPlayerAccountId: string;
    expiresAtUtc: string;
    createdAtUtc: string;
  },
): Promise<string> {
  const tokenPayload = `${input.battleId}:${input.requestedByPlayerAccountId}:${input.createdAtUtc}`;
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(tokenPayload),
  );
  const token = bytesToBase64(new Uint8Array(digest)).replace(/[+/=]/g, "").slice(0, 32);
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
      input.battleId,
      input.requestedByPlayerAccountId,
      input.expiresAtUtc,
      input.createdAtUtc,
      null,
      null,
    )
    .run();
  return token;
}

export async function getReplayToken(
  env: Env,
  token: string,
): Promise<{
  token: string;
  battle_id: string;
  requested_by_player_account_id: string;
  expires_at_utc: string;
  created_at_utc: string;
  used_at_utc: string | null;
  revoked_at_utc: string | null;
} | null> {
  return env.DB.prepare(
    `
      SELECT token, battle_id, requested_by_player_account_id, expires_at_utc, created_at_utc, used_at_utc, revoked_at_utc
      FROM replay_tokens
      WHERE token = ?
    `,
  )
    .bind(token)
    .first();
}
