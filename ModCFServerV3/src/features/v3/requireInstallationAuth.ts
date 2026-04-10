import type { Env } from "../../env";
import { json } from "../../http/json";
import {
  buildCanonicalRequest,
  computeRequestBodyHash,
  normalizeQueryString,
  verifyInstallationSignature,
} from "../../crypto/v3Signature";

type InstallationRow = {
  installation_id: string;
  player_account_id: string;
  public_key: string;
  status: string;
  revoked_at_utc: string | null;
};

export type InstallationAuthContext = {
  installationId: string;
  playerAccountId: string;
};

export async function requireInstallationAuth(
  request: Request,
  env: Env,
): Promise<InstallationAuthContext | Response> {
  const installationId = request.headers.get("x-bpp-installation-id")?.trim() ?? "";
  const timestamp = request.headers.get("x-bpp-timestamp")?.trim() ?? "";
  const bodyHash = request.headers.get("x-bpp-content-sha256")?.trim() ?? "";
  const signature = request.headers.get("x-bpp-signature")?.trim() ?? "";

  if (!installationId || !timestamp || !bodyHash || !signature) {
    return json({ error: "installation_auth_required" }, { status: 401 });
  }

  const installation = await env.DB.prepare(
    `
      SELECT
        installation_id,
        player_account_id,
        public_key,
        status,
        revoked_at_utc
      FROM installations
      WHERE installation_id = ?
    `,
  )
    .bind(installationId)
    .first<InstallationRow>();
  if (!installation || installation.status !== "active" || installation.revoked_at_utc != null) {
    return json({ error: "installation_not_active" }, { status: 403 });
  }

  const computedBodyHash = await computeRequestBodyHash(request);
  if (computedBodyHash !== bodyHash) {
    return json({ error: "content_hash_mismatch" }, { status: 401 });
  }

  const requestTimestamp = Date.parse(timestamp);
  if (!Number.isFinite(requestTimestamp) || Math.abs(Date.now() - requestTimestamp) > 5 * 60 * 1000) {
    return json({ error: "timestamp_out_of_window" }, { status: 401 });
  }

  const url = new URL(request.url);
  const canonical = buildCanonicalRequest({
    method: request.method,
    path: url.pathname,
    query: normalizeQueryString(url.searchParams),
    installationId,
    timestamp,
    bodyHash,
  });

  let publicKey: { modulus_b64: string; exponent_b64: string };
  try {
    publicKey = JSON.parse(installation.public_key) as {
      modulus_b64: string;
      exponent_b64: string;
    };
  } catch {
    return json({ error: "invalid_installation_public_key" }, { status: 500 });
  }

  const verified = await verifyInstallationSignature({
    publicKey,
    canonical,
    signatureB64: signature,
  });
  if (!verified) {
    return json({ error: "invalid_signature" }, { status: 401 });
  }

  return {
    installationId: installation.installation_id,
    playerAccountId: installation.player_account_id,
  };
}

