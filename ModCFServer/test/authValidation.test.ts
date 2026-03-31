import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import {
  canonicalRequest,
  generateClientKeyPair,
  sha256Base64,
  signCanonical,
  toBase64,
} from "./helpers/crypto";
import { buildEnv } from "./helpers/mockEnv";

function stringifyConsoleArgs(args: unknown[]): string {
  return args
    .map((value) =>
      typeof value === "string" ? value : JSON.stringify(value),
    )
    .join(" ");
}

async function captureWarnLogs<T>(
  run: () => Promise<T>,
): Promise<{ result: T; entries: string[] }> {
  const entries: string[] = [];
  const originalWarn = console.warn;
  console.warn = (...args: unknown[]) => {
    entries.push(stringifyConsoleArgs(args));
  };

  try {
    const result = await run();
    return { result, entries };
  } finally {
    console.warn = originalWarn;
  }
}

test("rejects battle uploads with an invalid body hash", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-bad-hash";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-002",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ battle_id: "battle-bad-hash" });
  const timestamp = new Date().toISOString();
  const nonce = "nonce-bad-hash";
  const wrongHash = toBase64("wrong-hash");
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/battles/upload",
      clientId,
      "install-002",
      timestamp,
      nonce,
      wrongHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/battles/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-002",
        "x-bpp-battle-id": "battle-bad-hash",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": wrongHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "body_hash_mismatch" });
  assert.equal(env.PVP_BATTLE_BUCKET.objects.size, 0);
});

test("accepts signed battle uploads without a nonce header", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-without-nonce";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-003",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    battle_id: "battle-without-nonce",
    replay_payload: {},
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/battles/upload",
      clientId,
      "install-003",
      timestamp,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/battles/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-003",
        "x-bpp-battle-id": "battle-without-nonce",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "battle_manifest_required" });
});

test("rejects battle uploads with an out-of-range timestamp", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-old-timestamp";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-004",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ battle_id: "battle-old-timestamp" });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date(Date.now() - 11 * 60 * 1000).toISOString();
  const nonce = "nonce-old-timestamp";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/battles/upload",
      clientId,
      "install-004",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/battles/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-004",
        "x-bpp-battle-id": "battle-old-timestamp",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "timestamp_out_of_range" });
});

test("rejects battle uploads with an invalid signature", async () => {
  const env = buildEnv();
  const { modulusB64, exponentB64 } = generateClientKeyPair();
  const { privateKey } = generateClientKeyPair();
  const clientId = "client-invalid-signature";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-005",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ battle_id: "battle-invalid-signature" });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-invalid-signature";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/battles/upload",
      clientId,
      "install-005",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/battles/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-005",
        "x-bpp-battle-id": "battle-invalid-signature",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "invalid_signature" });
});

test("auth rejection logs omit nonce values", async () => {
  const env = buildEnv();
  const nonce = "nonce-should-not-be-logged";

  const { result: response, entries } = await captureWarnLogs(() =>
    worker.fetch(
      new Request("https://example.com/battles/upload", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "x-bpp-battle-id": "battle-missing-signature-headers",
          "x-bpp-client-id": "client-missing-headers",
          "x-bpp-install-id": "install-missing-headers",
          "x-bpp-timestamp": new Date().toISOString(),
        },
        body: JSON.stringify({ battle_id: "battle-missing-signature-headers" }),
      }),
      env as never,
    ),
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "signed_headers_required" });
  assert.equal(entries.length, 1);
  assert.ok(entries[0]?.includes("\"event\":\"auth.rejected\""));
  assert.ok(!entries[0]?.includes("\"nonce\""));
  assert.ok(!entries[0]?.includes(nonce));
});
