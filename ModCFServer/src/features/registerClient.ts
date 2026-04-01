import { bytesToBase64 } from "../crypto/base64";
import type { Env } from "../env";
import { json, readJson } from "../http/json";
import { trimString } from "../http/request";
import { upsertRegisteredClient } from "../persistence/clients";
import type { RegisterRequest } from "../types/api";

export async function registerClient(
  request: Request,
  env: Env,
): Promise<Response> {
  const body = (await readJson(request)) as RegisterRequest;
  const installId = trimString(body.install_id);
  const modulusB64 = trimString(body.public_key?.modulus_b64);
  const exponentB64 = trimString(body.public_key?.exponent_b64);
  if (!installId) {
    return json({ error: "install_id_required" }, { status: 400 });
  }

  if (!modulusB64 || !exponentB64) {
    return json({ error: "public_key_required" }, { status: 400 });
  }

  const registeredAtUtc = new Date().toISOString();
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(
      [installId, modulusB64, exponentB64].join(":"),
    ),
  );
  const clientId = `client-${bytesToBase64(new Uint8Array(digest))
    .replace(/[+/=]/g, "")
    .slice(0, 24)}`;

  await upsertRegisteredClient(env, {
    clientId,
    installId,
    modulusB64,
    exponentB64,
    pluginVersion: trimString(body.plugin_version) || null,
    registeredAtUtc,
    lastSeenAtUtc: registeredAtUtc,
    revokedAtUtc: null,
  });

  return json({
    client_id: clientId,
    status: "registered",
  });
}
