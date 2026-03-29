import type { Env } from "../env";
import type { ReplayUploadLookupRow } from "../types/db";

export async function upsertReplayUpload(
  env: Env,
  input: {
    clientId: string;
    installId: string;
    battleId: string;
    runId: string | null;
    payloadSha256: string;
    objectKey: string;
    uploadedAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO replay_uploads (
        client_id,
        install_id,
        battle_id,
        run_id,
        payload_sha256,
        object_key,
        uploaded_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(battle_id) DO UPDATE SET
        client_id = excluded.client_id,
        install_id = excluded.install_id,
        run_id = excluded.run_id,
        payload_sha256 = excluded.payload_sha256,
        object_key = excluded.object_key,
        uploaded_at_utc = excluded.uploaded_at_utc
    `,
  )
    .bind(
      input.clientId,
      input.installId,
      input.battleId,
      input.runId,
      input.payloadSha256,
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
      SELECT battle_id, object_key, uploaded_at_utc
      FROM replay_uploads
      WHERE battle_id = ?
    `,
  )
    .bind(battleId)
    .first<ReplayUploadLookupRow>();
}
