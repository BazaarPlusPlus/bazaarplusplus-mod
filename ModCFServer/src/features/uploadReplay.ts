import type { Env } from "../env";
import { json } from "../http/json";
import { trimString } from "../http/request";
import { markBattleReplayAvailable } from "../persistence/battleProjections";
import { upsertReplayUpload } from "../persistence/replayUploads";
import { parseReplayUploadBody } from "./uploadReplayPayload";
import { requireVerifiedClient } from "./verifiedClient";

export async function handleReplayUpload(
  request: Request,
  env: Env,
): Promise<Response> {
  const battleId = trimString(request.headers.get("x-bpp-battle-id"));
  if (!battleId) {
    return json({ error: "battle_id_required" }, { status: 400 });
  }

  const verified = await requireVerifiedClient(request, env, "replays", {
    consumeNonce: true,
  });
  if (verified instanceof Response) {
    return verified;
  }

  const parsed = parseReplayUploadBody(verified.payload, battleId);
  if (parsed instanceof Response) {
    return parsed;
  }

  const runId = trimString(request.headers.get("x-bpp-run-id")) || null;
  const uploadedAtUtc = new Date().toISOString();
  const contentType = request.headers.get("content-type") ?? "application/json";
  const objectKey = `replays/${verified.client.client_id}/${battleId}/${verified.payloadHash}.json`;
  await env.REPLAY_BUCKET.put(objectKey, verified.payload, {
    httpMetadata: {
      contentType,
    },
  });

  await upsertReplayUpload(env, {
    clientId: verified.client.client_id,
    installId: verified.client.install_id,
    battleId,
    runId,
    payloadSha256: verified.payloadHash,
    objectKey,
    payloadBytes: verified.payload.byteLength,
    schemaVersion: parsed.schemaVersion,
    contentType,
    createdAtUtc: uploadedAtUtc,
    updatedAtUtc: uploadedAtUtc,
    uploadedAtUtc,
  });
  await markBattleReplayAvailable(env, battleId);

  return json({
    status: "accepted",
    object_key: objectKey,
  });
}
