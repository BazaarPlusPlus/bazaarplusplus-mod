import type { Env } from "../env";
import type {
  ActiveBindingPlayerAccountRow,
} from "../types/db";

export async function getActiveBoundPlayerAccountId(
  env: Env,
  clientId: string,
): Promise<string | null> {
  const row = await env.DB.prepare(
    `
      SELECT player_account_id
      FROM client_player_account_bindings
      WHERE client_id = ?
        AND unbound_at_utc IS NULL
      ORDER BY bound_at_utc DESC
      LIMIT 1
    `,
  )
    .bind(clientId)
    .first<ActiveBindingPlayerAccountRow>();

  return row?.player_account_id ?? null;
}

export async function upsertClientPlayerAccountBinding(
  env: Env,
  input: {
    bindingId: string;
    clientId: string;
    playerAccountId: string;
    bindingSource: string;
    confidence: number;
    boundAtUtc: string;
  },
): Promise<void> {
  const existingBinding = await env.DB.prepare(
    `
      SELECT player_account_id
      FROM client_player_account_bindings
      WHERE client_id = ?
        AND unbound_at_utc IS NULL
      ORDER BY bound_at_utc DESC
      LIMIT 1
    `,
  )
    .bind(input.clientId)
    .first<ActiveBindingPlayerAccountRow>();

  if (existingBinding?.player_account_id === input.playerAccountId) {
    return;
  }

  await env.DB.prepare(
    `
      UPDATE client_player_account_bindings
      SET unbound_at_utc = ?
      WHERE client_id = ?
        AND unbound_at_utc IS NULL
    `,
  )
    .bind(input.boundAtUtc, input.clientId)
    .run();

  await env.DB.prepare(
    `
      INSERT INTO client_player_account_bindings (
        binding_id,
        client_id,
        player_account_id,
        binding_source,
        confidence,
        bound_at_utc,
        unbound_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, NULL)
    `,
  )
    .bind(
      input.bindingId,
      input.clientId,
      input.playerAccountId,
      input.bindingSource,
      input.confidence,
      input.boundAtUtc,
    )
    .run();
}

export async function upsertClientPlayerAccountObservation(
  env: Env,
  input: {
    clientId: string;
    playerAccountId: string;
    observedAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO client_player_account_observations (
        client_id,
        player_account_id,
        first_seen_at_utc,
        last_seen_at_utc
      ) VALUES (?, ?, ?, ?)
      ON CONFLICT(client_id, player_account_id) DO UPDATE SET
        last_seen_at_utc = excluded.last_seen_at_utc,
        evidence_count = evidence_count + 1
    `,
  )
    .bind(
      input.clientId,
      input.playerAccountId,
      input.observedAtUtc,
      input.observedAtUtc,
  )
    .run();
}
