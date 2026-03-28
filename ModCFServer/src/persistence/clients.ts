import type { Env } from "../env";
import type { UploadPurpose } from "../types/api";
import type { RegisteredClientRow } from "../types/db";

export async function upsertRegisteredClient(
  env: Env,
  input: {
    clientId: string;
    installId: string;
    purpose: UploadPurpose;
    modulusB64: string;
    exponentB64: string;
    pluginVersion: string | null;
    registeredAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO registered_clients (
        client_id,
        install_id,
        purpose,
        modulus_b64,
        exponent_b64,
        plugin_version,
        registered_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(client_id) DO UPDATE SET
        install_id = excluded.install_id,
        purpose = excluded.purpose,
        modulus_b64 = excluded.modulus_b64,
        exponent_b64 = excluded.exponent_b64,
        plugin_version = excluded.plugin_version,
        registered_at_utc = excluded.registered_at_utc
    `,
  )
    .bind(
      input.clientId,
      input.installId,
      input.purpose,
      input.modulusB64,
      input.exponentB64,
      input.pluginVersion,
      input.registeredAtUtc,
    )
    .run();
}

export async function getRegisteredClient(
  env: Env,
  clientId: string,
): Promise<RegisteredClientRow | null> {
  return env.DB.prepare(
    `
      SELECT client_id, install_id, purpose, modulus_b64, exponent_b64, plugin_version
      FROM registered_clients
      WHERE client_id = ?
    `,
  )
    .bind(clientId)
    .first<RegisteredClientRow>();
}
