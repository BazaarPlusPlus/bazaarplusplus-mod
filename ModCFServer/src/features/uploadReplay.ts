import type { Env } from "../env";
import { json } from "../http/json";
import { trimString } from "../http/request";
import { upsertReplayUpload } from "../persistence/replayUploads";
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

  const runId = trimString(request.headers.get("x-bpp-run-id")) || null;
  const objectKey = `combat-replays/replays/${verified.client.client_id}/${battleId}/${verified.payloadHash
    .replace(/[+/=]/g, "")
    .slice(0, 16)}.payload.json`;
  await env.REPLAY_BUCKET.put(objectKey, verified.payload, {
    httpMetadata: {
      contentType: request.headers.get("content-type") ?? "application/json",
    },
  });

  await upsertReplayUpload(env, {
    clientId: verified.client.client_id,
    installId: verified.client.install_id,
    battleId,
    runId,
    payloadSha256: verified.payloadHash,
    objectKey,
    uploadedAtUtc: new Date().toISOString(),
  });

  return json({
    status: "accepted",
    object_key: objectKey,
  });
}
