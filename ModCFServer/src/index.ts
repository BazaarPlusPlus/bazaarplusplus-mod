export interface Env {
  DB: D1Database;
  REPLAY_BUCKET: R2Bucket;
}

type UploadPurpose = "runs" | "replays";

type RegisterRequest = {
  install_id?: unknown;
  plugin_version?: unknown;
  purpose?: unknown;
  public_key?: {
    modulus_b64?: unknown;
    exponent_b64?: unknown;
  };
};

type RegisteredClientRow = {
  client_id: string;
  install_id: string;
  purpose: UploadPurpose;
  modulus_b64: string;
  exponent_b64: string;
  plugin_version: string | null;
};

const MAX_TIMESTAMP_SKEW_MS = 10 * 60 * 1000;

function json(data: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(data), {
    ...init,
    headers: {
      "content-type": "application/json; charset=utf-8",
      ...(init?.headers ?? {}),
    },
  });
}

async function readJson(request: Request): Promise<unknown> {
  const contentType = request.headers.get("content-type") ?? "";
  if (!contentType.includes("application/json")) {
    throw new Response("expected application/json", { status: 415 });
  }

  return request.json();
}

function trimString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function normalizePurpose(value: unknown): UploadPurpose | null {
  const normalized = trimString(value).toLowerCase();
  if (normalized === "runs" || normalized === "replays") {
    return normalized;
  }

  return null;
}

function absolutePath(request: Request): string {
  return new URL(request.url).pathname || "/";
}

function canonicalRequest(
  method: string,
  path: string,
  clientId: string,
  installId: string,
  timestamp: string,
  nonce: string,
  bodyHash: string,
): string {
  return [
    method.trim().toUpperCase(),
    path.trim() || "/",
    clientId.trim(),
    installId.trim(),
    timestamp.trim(),
    nonce.trim(),
    bodyHash.trim(),
  ].join("\n");
}

function base64ToBytes(base64: string): Uint8Array {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  return bytes;
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (const value of bytes) {
    binary += String.fromCharCode(value);
  }

  return btoa(binary);
}

function toBase64Url(base64: string): string {
  return base64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

async function sha256Base64(payload: ArrayBuffer): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", payload);
  return bytesToBase64(new Uint8Array(digest));
}

async function verifySignature(
  modulusB64: string,
  exponentB64: string,
  canonical: string,
  signatureB64: string,
): Promise<boolean> {
  const key = await crypto.subtle.importKey(
    "jwk",
    {
      kty: "RSA",
      n: toBase64Url(modulusB64),
      e: toBase64Url(exponentB64),
      alg: "RS256",
      ext: true,
    },
    {
      name: "RSASSA-PKCS1-v1_5",
      hash: "SHA-256",
    },
    false,
    ["verify"],
  );

  return crypto.subtle.verify(
    "RSASSA-PKCS1-v1_5",
    key,
    base64ToBytes(signatureB64),
    new TextEncoder().encode(canonical),
  );
}

async function ensureSchema(env: Env): Promise<void> {
  await env.DB.prepare(
    `
      CREATE TABLE IF NOT EXISTS registered_clients (
        client_id TEXT PRIMARY KEY,
        install_id TEXT NOT NULL,
        purpose TEXT NOT NULL,
        modulus_b64 TEXT NOT NULL,
        exponent_b64 TEXT NOT NULL,
        plugin_version TEXT NULL,
        registered_at_utc TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS replay_uploads (
        battle_id TEXT PRIMARY KEY,
        client_id TEXT NOT NULL,
        install_id TEXT NOT NULL,
        run_id TEXT NULL,
        payload_sha256 TEXT NOT NULL,
        object_key TEXT NOT NULL,
        uploaded_at_utc TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS run_uploads (
        run_id TEXT PRIMARY KEY,
        client_id TEXT NOT NULL,
        install_id TEXT NOT NULL,
        payload_sha256 TEXT NOT NULL,
        uploaded_at_utc TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS request_nonces (
        nonce_key TEXT PRIMARY KEY,
        created_at_utc TEXT NOT NULL
      );
    `,
  ).run();
}

async function registerClient(request: Request, env: Env): Promise<Response> {
  const body = (await readJson(request)) as RegisterRequest;
  const installId = trimString(body.install_id);
  const purpose = normalizePurpose(body.purpose) ?? "runs";
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
      [purpose, installId, modulusB64, exponentB64].join(":"),
    ),
  );
  const clientId = `${purpose}-${bytesToBase64(new Uint8Array(digest))
    .replace(/[+/=]/g, "")
    .slice(0, 24)}`;

  await env.DB.prepare(
    `
      INSERT INTO registered_clients (
        client_id,
        install_id,
        purpose,
        modulus_b64,
        exponent_b64,
        plugin_version,
        registered_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(client_id) DO UPDATE SET
        install_id = excluded.install_id,
        purpose = excluded.purpose,
        modulus_b64 = excluded.modulus_b64,
        exponent_b64 = excluded.exponent_b64,
        plugin_version = excluded.plugin_version,
        registered_at_utc = excluded.registered_at_utc
    `,
  )
    .bind(
      clientId,
      installId,
      purpose,
      modulusB64,
      exponentB64,
      trimString(body.plugin_version) || null,
      registeredAtUtc,
    )
    .run();

  return json({
    client_id: clientId,
    purpose,
    status: "registered",
  });
}

async function requireVerifiedClient(
  request: Request,
  env: Env,
  purpose: UploadPurpose,
): Promise<
  | {
      client: RegisteredClientRow;
      payload: ArrayBuffer;
      payloadHash: string;
      timestamp: string;
      nonce: string;
    }
  | Response
> {
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

  const nonceKey = `${purpose}:${clientId}:${nonce}`;
  const existingNonce = await env.DB.prepare(
    "SELECT nonce_key FROM request_nonces WHERE nonce_key = ?",
  )
    .bind(nonceKey)
    .first<{ nonce_key: string }>();
  if (existingNonce) {
    return json({ error: "nonce_reused" }, { status: 409 });
  }

  const client = await env.DB.prepare(
    `
      SELECT client_id, install_id, purpose, modulus_b64, exponent_b64, plugin_version
      FROM registered_clients
      WHERE client_id = ?
    `,
  )
    .bind(clientId)
    .first<RegisteredClientRow>();
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

  await env.DB.prepare(
    "INSERT INTO request_nonces (nonce_key, created_at_utc) VALUES (?, ?)",
  )
    .bind(nonceKey, new Date().toISOString())
    .run();

  return { client, payload, payloadHash, timestamp, nonce };
}

async function handleReplayUpload(request: Request, env: Env): Promise<Response> {
  const battleId = trimString(request.headers.get("x-bpp-battle-id"));
  if (!battleId) {
    return json({ error: "battle_id_required" }, { status: 400 });
  }

  const verified = await requireVerifiedClient(request, env, "replays");
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
      verified.client.client_id,
      verified.client.install_id,
      battleId,
      runId,
      verified.payloadHash,
      objectKey,
      new Date().toISOString(),
    )
    .run();

  return json({
    status: "accepted",
    object_key: objectKey,
  });
}

async function handleRunUpload(request: Request, env: Env): Promise<Response> {
  const verified = await requireVerifiedClient(request, env, "runs");
  if (verified instanceof Response) {
    return verified;
  }

  const runId = trimString(request.headers.get("x-bpp-run-id")) || null;
  await env.DB.prepare(
    `
      INSERT INTO run_uploads (
        client_id,
        install_id,
        run_id,
        payload_sha256,
        uploaded_at_utc
      ) VALUES (?, ?, ?, ?, ?)
      ON CONFLICT(run_id) DO UPDATE SET
        client_id = excluded.client_id,
        install_id = excluded.install_id,
        payload_sha256 = excluded.payload_sha256,
        uploaded_at_utc = excluded.uploaded_at_utc
    `,
  )
    .bind(
      verified.client.client_id,
      verified.client.install_id,
      runId,
      verified.payloadHash,
      new Date().toISOString(),
    )
    .run();

  return json({ status: "accepted" });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    await ensureSchema(env);
    const url = new URL(request.url);

    if (request.method === "POST" && url.pathname === "/health") {
      return json({ ok: true });
    }

    if (request.method === "POST" && url.pathname === "/clients/register") {
      return registerClient(request, env);
    }

    if (request.method === "POST" && url.pathname === "/runs/upload") {
      return handleRunUpload(request, env);
    }

    if (request.method === "POST" && url.pathname === "/replays/upload") {
      return handleReplayUpload(request, env);
    }

    return json({ error: "not_found" }, { status: 404 });
  },
};
