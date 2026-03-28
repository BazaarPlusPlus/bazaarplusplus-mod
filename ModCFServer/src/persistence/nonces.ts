import type { Env } from "../env";

export async function hasSeenNonce(
  env: Env,
  nonceKey: string,
): Promise<boolean> {
  const existing = await env.DB.prepare(
    "SELECT nonce_key FROM request_nonces WHERE nonce_key = ?",
  )
    .bind(nonceKey)
    .first<{ nonce_key: string }>();

  return existing != null;
}

export async function storeNonce(
  env: Env,
  nonceKey: string,
  createdAtUtc: string,
): Promise<void> {
  await env.DB.prepare(
    "INSERT INTO request_nonces (nonce_key, created_at_utc) VALUES (?, ?)",
  )
    .bind(nonceKey, createdAtUtc)
    .run();
}
