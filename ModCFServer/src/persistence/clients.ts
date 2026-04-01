import type { Env } from "../env";
import type { ClientRow } from "../types/db";

export async function upsertRegisteredClient(
  env: Env,
  input: {
    clientId: string;
    installId: string;
    modulusB64: string;
    exponentB64: string;
    pluginVersion: string | null;
    registeredAtUtc: string;
    lastSeenAtUtc?: string | null;
    revokedAtUtc?: string | null;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO clients (
        client_id,
        install_id,
        modulus_b64,
        exponent_b64,
        plugin_version,
        registered_at_utc,
        last_seen_at_utc,
        revoked_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(client_id) DO UPDATE SET
        install_id = excluded.install_id,
        modulus_b64 = excluded.modulus_b64,
        exponent_b64 = excluded.exponent_b64,
        plugin_version = excluded.plugin_version,
        registered_at_utc = excluded.registered_at_utc,
        last_seen_at_utc = excluded.last_seen_at_utc,
        revoked_at_utc = excluded.revoked_at_utc
    `,
  )
    .bind(
      input.clientId,
      input.installId,
      input.modulusB64,
      input.exponentB64,
      input.pluginVersion,
      input.registeredAtUtc,
      input.lastSeenAtUtc ?? null,
      input.revokedAtUtc ?? null,
    )
    .run();
}

export async function getRegisteredClient(
  env: Env,
  clientId: string,
): Promise<ClientRow | null> {
  return env.DB.prepare(
    `
      SELECT client_id, install_id, modulus_b64, exponent_b64, plugin_version, registered_at_utc, last_seen_at_utc, revoked_at_utc
      FROM clients
      WHERE client_id = ?
    `,
  )
    .bind(clientId)
    .first<ClientRow>();
}
