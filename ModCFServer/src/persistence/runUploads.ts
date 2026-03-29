import type { Env } from "../env";
import type { RunUploadProjectionStatus } from "../types/db";

export async function upsertRunUpload(
  env: Env,
  input: {
    clientId: string;
    installId: string;
    runId: string;
    payloadSha256: string;
    uploadedAtUtc: string;
    projectionStatus: RunUploadProjectionStatus;
    projectedAtUtc?: string | null;
    projectionError?: string | null;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      INSERT INTO run_uploads (
        client_id,
        install_id,
        run_id,
        payload_sha256,
        uploaded_at_utc,
        projection_status,
        projected_at_utc,
        projection_error
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(run_id) DO UPDATE SET
        client_id = excluded.client_id,
        install_id = excluded.install_id,
        payload_sha256 = excluded.payload_sha256,
        uploaded_at_utc = excluded.uploaded_at_utc,
        projection_status = excluded.projection_status,
        projected_at_utc = excluded.projected_at_utc,
        projection_error = excluded.projection_error
    `,
  )
    .bind(
      input.clientId,
      input.installId,
      input.runId,
      input.payloadSha256,
      input.uploadedAtUtc,
      input.projectionStatus,
      input.projectedAtUtc ?? null,
      input.projectionError ?? null,
    )
    .run();
}

export async function markRunProjectionStatus(
  env: Env,
  input: {
    runId: string;
    projectionStatus: RunUploadProjectionStatus;
    projectedAtUtc?: string | null;
    projectionError?: string | null;
  },
): Promise<void> {
  await env.DB.prepare(
    `
      UPDATE run_uploads
      SET projection_status = ?,
          projected_at_utc = ?,
          projection_error = ?
      WHERE run_id = ?
    `,
  )
    .bind(
      input.projectionStatus,
      input.projectedAtUtc ?? null,
      input.projectionError ?? null,
      input.runId,
    )
    .run();
}
