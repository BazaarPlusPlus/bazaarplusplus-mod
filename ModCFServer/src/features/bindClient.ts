import { bytesToBase64 } from "../crypto/base64";
import type { Env } from "../env";
import { json } from "../http/json";
import { trimString } from "../http/request";
import {
  upsertClientPlayerAccountBinding,
  upsertClientPlayerAccountObservation,
} from "../persistence/bindings";
import { requireVerifiedClient } from "./verifiedClient";

type BindClientRequest = {
  player_account_id?: unknown;
  observed_player_account_id?: unknown;
};

export async function handleBindClient(
  request: Request,
  env: Env,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env, "runs", {
    consumeNonce: true,
  });
  if (verified instanceof Response) {
    return verified;
  }

  const contentType = request.headers.get("content-type") ?? "";
  if (!contentType.includes("application/json")) {
    return json({ error: "expected_application_json" }, { status: 415 });
  }

  let body: BindClientRequest;
  try {
    body = JSON.parse(new TextDecoder().decode(verified.payload)) as BindClientRequest;
  } catch {
    return json({ error: "invalid_json" }, { status: 400 });
  }

  const playerAccountId = trimString(body.player_account_id);
  const observedPlayerAccountId = trimString(body.observed_player_account_id);
  if (!playerAccountId) {
    return json({ error: "player_account_id_required" }, { status: 400 });
  }

  const boundAtUtc = new Date().toISOString();
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(
      [verified.client.client_id, playerAccountId, boundAtUtc].join(":"),
    ),
  );

  await upsertClientPlayerAccountBinding(env, {
    bindingId: `binding-${bytesToBase64(new Uint8Array(digest)).replace(/[+/=]/g, "").slice(0, 24)}`,
    clientId: verified.client.client_id,
    playerAccountId,
    bindingSource: "client_bind",
    confidence: 100,
    boundAtUtc,
  });

  if (observedPlayerAccountId) {
    await upsertClientPlayerAccountObservation(env, {
      clientId: verified.client.client_id,
      playerAccountId: observedPlayerAccountId,
      observedAtUtc: boundAtUtc,
    });
  }

  return json({
    status: "bound",
    player_account_id: playerAccountId,
  });
}
