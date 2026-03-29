import type { Env } from "../env";

export async function tryConsumeNonce(
  env: Env,
  nonceKey: string,
  createdAtUtc: string,
): Promise<boolean> {
  const result = await env.DB.prepare(
    "INSERT INTO request_nonces (nonce_key, created_at_utc) VALUES (?, ?) ON CONFLICT(nonce_key) DO NOTHING",
  )
    .bind(nonceKey, createdAtUtc)
    .run();

  return (result.meta.changes ?? 0) > 0;
}

export async function purgeExpiredNonces(
  env: Env,
  maxAgeMs: number,
): Promise<void> {
  const cutoff = new Date(Date.now() - maxAgeMs).toISOString();
  await env.DB.prepare(
    "DELETE FROM request_nonces WHERE created_at_utc < ?",
  )
    .bind(cutoff)
    .run();
}
