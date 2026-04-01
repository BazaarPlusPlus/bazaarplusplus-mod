import type { Env } from "../env";

export async function upsertRun(
  env: Env,
  input: {
    runId: string;
    clientId: string;
    playerAccountId: string | null;
    status: string;
    heroId: string | null;
    heroName: string | null;
    startedAtUtc: string | null;
    endedAtUtc: string;
    finalDay: number | null;
    finalWins: number | null;
    finalLosses: number | null;
    mmr: number | null;
    summarySchemaVersion: number | null;
    summaryObjectKey: string | null;
    createdAtUtc: string;
    updatedAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO runs (
        run_id,
        client_id,
        player_account_id,
        status,
        hero_id,
        hero_name,
        started_at_utc,
        ended_at_utc,
        final_day,
        final_wins,
        final_losses,
        mmr,
        summary_schema_version,
        summary_object_key,
        created_at_utc,
        updated_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(run_id) DO UPDATE SET
        client_id = excluded.client_id,
        player_account_id = excluded.player_account_id,
        status = excluded.status,
        hero_id = excluded.hero_id,
        hero_name = excluded.hero_name,
        started_at_utc = excluded.started_at_utc,
        ended_at_utc = excluded.ended_at_utc,
        final_day = excluded.final_day,
        final_wins = excluded.final_wins,
        final_losses = excluded.final_losses,
        mmr = excluded.mmr,
        summary_schema_version = excluded.summary_schema_version,
        summary_object_key = excluded.summary_object_key,
        updated_at_utc = excluded.updated_at_utc
    `,
  )
    .bind(
      input.runId,
      input.clientId,
      input.playerAccountId,
      input.status,
      input.heroId,
      input.heroName,
      input.startedAtUtc,
      input.endedAtUtc,
      input.finalDay,
      input.finalWins,
      input.finalLosses,
      input.mmr,
      input.summarySchemaVersion,
      input.summaryObjectKey,
      input.createdAtUtc,
      input.updatedAtUtc,
    )
    .run();
}
