import type { Env } from "../env";
import type { PlayerLinkRow } from "../types/db";

export async function upsertPlayerLink(
  env: Env,
  input: {
    clientId: string;
    playerAccountId: string;
    boundAtUtc: string;
    lastConfirmedAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO player_links (
        client_id,
        player_account_id,
        bound_at_utc,
        last_confirmed_at_utc
      ) VALUES (?, ?, ?, ?)
      ON CONFLICT(client_id) DO UPDATE SET
        player_account_id = excluded.player_account_id,
        bound_at_utc = excluded.bound_at_utc,
        last_confirmed_at_utc = excluded.last_confirmed_at_utc
    `,
  )
    .bind(
      input.clientId,
      input.playerAccountId,
      input.boundAtUtc,
      input.lastConfirmedAtUtc,
    )
    .run();
}

export async function getPlayerLink(
  env: Env,
  clientId: string,
): Promise<PlayerLinkRow | null> {
  return env.DB.prepare(
    `
      SELECT client_id, player_account_id, bound_at_utc, last_confirmed_at_utc
      FROM player_links
      WHERE client_id = ?
    `,
  )
    .bind(clientId)
    .first<PlayerLinkRow>();
}
