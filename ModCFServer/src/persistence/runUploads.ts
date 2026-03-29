import type { Env } from "../env";
import type { RunUploadProjectionStatus } from "../types/db";

export async function upsertRunUpload(
  env: Env,
  input: {
    clientId: string;
    installId: string;
    runId: string;
    payloadSha256: string;
    payloadObjectKey: string | null;
    payloadBytes: number | null;
    schemaVersion: number | null;
    projectionStatus: RunUploadProjectionStatus;
    projectedAtUtc?: string | null;
    lastErrorCode?: string | null;
    lastErrorDetail?: string | null;
    createdAtUtc: string;
    updatedAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO run_uploads (
        client_id,
        install_id,
        run_id,
        payload_sha256,
        payload_object_key,
        payload_bytes,
        schema_version,
        projection_status,
        projected_at_utc,
        last_error_code,
        last_error_detail,
        created_at_utc,
        updated_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(run_id) DO UPDATE SET
        client_id = excluded.client_id,
        install_id = excluded.install_id,
        payload_sha256 = excluded.payload_sha256,
        payload_object_key = excluded.payload_object_key,
        payload_bytes = excluded.payload_bytes,
        schema_version = excluded.schema_version,
        projection_status = excluded.projection_status,
        projected_at_utc = excluded.projected_at_utc,
        last_error_code = excluded.last_error_code,
        last_error_detail = excluded.last_error_detail,
        updated_at_utc = excluded.updated_at_utc
    `,
  )
    .bind(
      input.clientId,
      input.installId,
      input.runId,
      input.payloadSha256,
      input.payloadObjectKey,
      input.payloadBytes,
      input.schemaVersion,
      input.projectionStatus,
      input.projectedAtUtc ?? null,
      input.lastErrorCode ?? null,
      input.lastErrorDetail ?? null,
      input.createdAtUtc,
      input.updatedAtUtc,
    )
    .run();
}

export async function markRunProjectionStatus(
  env: Env,
  input: {
    runId: string;
    projectionStatus: RunUploadProjectionStatus;
    projectedAtUtc?: string | null;
    lastErrorCode?: string | null;
    lastErrorDetail?: string | null;
    updatedAtUtc: string;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      UPDATE run_uploads
      SET projection_status = ?,
          projected_at_utc = ?,
          last_error_code = ?,
          last_error_detail = ?,
          updated_at_utc = ?
      WHERE run_id = ?
    `,
  )
    .bind(
      input.projectionStatus,
      input.projectedAtUtc ?? null,
      input.lastErrorCode ?? null,
      input.lastErrorDetail ?? null,
      input.updatedAtUtc,
      input.runId,
    )
    .run();
}
