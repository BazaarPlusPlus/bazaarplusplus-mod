import { sha256Base64 } from "../crypto/hash";
import { canonicalRequest, verifySignature } from "../crypto/signature";
import type { Env } from "../env";
import { json } from "../http/json";
import { absolutePath, trimString } from "../http/request";
import { getRegisteredClient } from "../persistence/clients";
import { tryConsumeNonce } from "../persistence/nonces";
import type { UploadPurpose } from "../types/api";
import type { RegisteredClientRow } from "../types/db";

const MAX_TIMESTAMP_SKEW_MS = 10 * 60 * 1000;

type RequireVerifiedClientOptions = {
  consumeNonce?: boolean;
};

export type VerifiedClientRequest = {
  client: RegisteredClientRow;
  payload: ArrayBuffer;
  payloadHash: string;
  timestamp: string;
  nonce: string;
};

export async function requireVerifiedClient(
  request: Request,
  env: Env,
  purpose: UploadPurpose,
  options?: RequireVerifiedClientOptions,
): Promise<VerifiedClientRequest | Response> {
  const clientId = trimString(request.headers.get("x-bpp-client-id"));
  const installId = trimString(request.headers.get("x-bpp-install-id"));
  const timestamp = trimString(request.headers.get("x-bpp-timestamp"));
  const nonce = trimString(request.headers.get("x-bpp-nonce"));
  const advertisedBodyHash = trimString(request.headers.get("x-bpp-content-sha256"));
  const signatureAlg = trimString(request.headers.get("x-bpp-signature-alg"));
  const signature = trimString(request.headers.get("x-bpp-signature"));
  if (!clientId || !installId || !timestamp || !nonce || !advertisedBodyHash || !signature) {
    return json({ error: "signed_headers_required" }, { status: 401 });
  }

  if (signatureAlg && signatureAlg !== "rsa-pkcs1-sha256") {
    return json({ error: "unsupported_signature_alg" }, { status: 401 });
  }

  const requestTimestamp = Date.parse(timestamp);
  if (
    Number.isNaN(requestTimestamp) ||
    Math.abs(Date.now() - requestTimestamp) > MAX_TIMESTAMP_SKEW_MS
  ) {
    return json({ error: "timestamp_out_of_range" }, { status: 401 });
  }

  const client = await getRegisteredClient(env, clientId);
  if (!client || client.install_id !== installId || client.purpose !== purpose) {
    return json({ error: "unknown_client" }, { status: 404 });
  }

  const payload = await request.arrayBuffer();
  const payloadHash = await sha256Base64(payload);
  if (payloadHash !== advertisedBodyHash) {
    return json({ error: "body_hash_mismatch" }, { status: 401 });
  }

  const canonical = canonicalRequest(
    request.method,
    absolutePath(request),
    clientId,
    installId,
    timestamp,
    nonce,
    advertisedBodyHash,
  );
  const signatureValid = await verifySignature(
    client.modulus_b64,
    client.exponent_b64,
    canonical,
    signature,
  );
  if (!signatureValid) {
    return json({ error: "invalid_signature" }, { status: 401 });
  }

  if (options?.consumeNonce ?? false) {
    const nonceKey = `${purpose}:${clientId}:${nonce}`;
    const consumed = await tryConsumeNonce(env, nonceKey, new Date().toISOString());
    if (!consumed) {
      return json({ error: "nonce_reused" }, { status: 409 });
    }
  }

  return { client, payload, payloadHash, timestamp, nonce };
}
