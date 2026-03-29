import type { Env } from "../env";
import { json } from "../http/json";
import { trimString } from "../http/request";
import {
  markRunProjectionStatus,
  upsertRunUpload,
} from "../persistence/runUploads";
import {
  parseRunUploadBody,
} from "./uploadRunPayload";
import { requireVerifiedClient } from "./verifiedClient";

export async function handleRunUpload(
  request: Request,
  env: Env,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env, "runs", {
    consumeNonce: true,
  });
  if (verified instanceof Response) {
    return verified;
  }

  const parsed = parseRunUploadBody(verified.payload);
  if (parsed instanceof Response) {
    return parsed;
  }

  const headerRunId = trimString(request.headers.get("x-bpp-run-id")) || null;
  const runId = headerRunId ?? parsed.runId;
  if (!runId) {
    return json({ error: "run_id_required" }, { status: 400 });
  }

  if (headerRunId && parsed.runId && headerRunId !== parsed.runId) {
    return json({ error: "run_id_mismatch" }, { status: 400 });
  }

  const receivedAtUtc = new Date().toISOString();
  const payloadObjectKey = `runs/${verified.client.client_id}/${runId}/${verified.payloadHash}.json`;
  const payloadBytes = verified.payload.byteLength;
  await upsertRunUpload(env, {
    clientId: verified.client.client_id,
    installId: verified.client.install_id,
    runId,
    payloadSha256: verified.payloadHash,
    payloadObjectKey,
    payloadBytes,
    schemaVersion: parsed.schemaVersion,
    projectionStatus: "received",
    projectedAtUtc: null,
    lastErrorCode: null,
    lastErrorDetail: null,
    createdAtUtc: receivedAtUtc,
    updatedAtUtc: receivedAtUtc,
  });

  try {
    await env.REPLAY_BUCKET.put(payloadObjectKey, verified.payload, {
      httpMetadata: {
        contentType: "application/json; charset=utf-8",
      },
      customMetadata: {
        "payload-sha256": verified.payloadHash,
        "client-id": verified.client.client_id,
        "run-id": runId,
        "uploaded-at-utc": receivedAtUtc,
      },
    });

    await markRunProjectionStatus(env, {
      runId,
      projectionStatus: "stored",
      projectedAtUtc: null,
      lastErrorCode: null,
      lastErrorDetail: null,
      updatedAtUtc: new Date().toISOString(),
    });

    await markRunProjectionStatus(env, {
      runId,
      projectionStatus: "projecting",
      projectedAtUtc: null,
      lastErrorCode: null,
      lastErrorDetail: null,
      updatedAtUtc: new Date().toISOString(),
    });

    const observedAtUtc = new Date().toISOString();

    await markRunProjectionStatus(env, {
      runId,
      projectionStatus: "projected",
      projectedAtUtc: observedAtUtc,
      lastErrorCode: null,
      lastErrorDetail: null,
      updatedAtUtc: observedAtUtc,
    });
  } catch (error) {
    const message =
      error instanceof Error ? error.message : "Unknown projection error";
    const failedAtUtc = new Date().toISOString();
    await markRunProjectionStatus(env, {
      runId,
      projectionStatus: "failed",
      projectedAtUtc: null,
      lastErrorCode: "projection_failed",
      lastErrorDetail: message,
      updatedAtUtc: failedAtUtc,
    });
    return json({ error: "projection_failed" }, { status: 500 });
  }

  return json({ status: "accepted" });
}
