import type { Env } from "../env";
import type {
  ActiveBindingUidRow,
  ObservedPlayerAccountRow,
} from "../types/db";

export async function getActiveBindingUid(
  env: Env,
  clientId: string,
): Promise<string | null> {
  const row = await env.DB.prepare(
    `
      SELECT uid
      FROM client_uid_bindings
      WHERE client_id = ?
        AND unbound_at_utc IS NULL
      ORDER BY bound_at_utc DESC
      LIMIT 1
    `,
  )
    .bind(clientId)
    .first<ActiveBindingUidRow>();

  return row?.uid ?? null;
}

export async function upsertObservedPlayerAccount(
  env: Env,
  input: {
    uid: string;
    playerAccountId: string;
    lastClientId: string;
    observedAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO uid_player_accounts (
        uid,
        player_account_id,
        first_seen_at_utc,
        last_seen_at_utc,
        last_client_id
      ) VALUES (?, ?, ?, ?, ?)
      ON CONFLICT(uid, player_account_id) DO UPDATE SET
        last_seen_at_utc = excluded.last_seen_at_utc,
        last_client_id = excluded.last_client_id
    `,
  )
    .bind(
      input.uid,
      input.playerAccountId,
      input.observedAtUtc,
      input.observedAtUtc,
      input.lastClientId,
    )
    .run();
}

export async function listObservedPlayerAccountIds(
  env: Env,
  uid: string,
): Promise<string[]> {
  const result = await env.DB.prepare(
    `
      SELECT player_account_id
      FROM uid_player_accounts
      WHERE uid = ?
      ORDER BY last_seen_at_utc DESC, player_account_id ASC
    `,
  )
    .bind(uid)
    .all<ObservedPlayerAccountRow>();

  return (result.results ?? []).map((row) => row.player_account_id);
}
