import type { Env } from "../env";
import type { ReplayUploadLookupRow } from "../types/db";

export async function upsertReplayUpload(
  env: Env,
  input: {
    battleId: string;
    objectKey: string;
    uploadedAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO replay_uploads (
        battle_id,
        object_key,
        uploaded_at_utc
      ) VALUES (?, ?, ?)
      ON CONFLICT(battle_id) DO UPDATE SET
        object_key = excluded.object_key,
        uploaded_at_utc = excluded.uploaded_at_utc
    `,
  )
    .bind(
      input.battleId,
      input.objectKey,
      input.uploadedAtUtc,
    )
    .run();
}

export async function getReplayUploadByBattleId(
  env: Env,
  battleId: string,
): Promise<ReplayUploadLookupRow | null> {
  return env.DB.prepare(
    `
      SELECT
        battle_id,
        object_key,
        uploaded_at_utc
      FROM replay_uploads
      WHERE battle_id = ?
    `,
  )
    .bind(battleId)
    .first<ReplayUploadLookupRow>();
}
