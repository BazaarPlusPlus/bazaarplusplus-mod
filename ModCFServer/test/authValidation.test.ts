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

test("rejects replay uploads with an invalid body hash", async () => {
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
      "/replays/upload",
      clientId,
      "install-002",
      timestamp,
      nonce,
      wrongHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-002",
        "x-bpp-battle-id": "battle-bad-hash",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-nonce": nonce,
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
  assert.equal(env.DB.replayUploads.size, 0);
  assert.equal(env.REPLAY_BUCKET.objects.size, 0);
});

test("rejects replay uploads when the nonce is reused", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-reused-nonce";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-003",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ battle_id: "battle-reused-nonce" });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-reused";
  env.DB.nonces.add(`replays:${clientId}:${nonce}`);
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/replays/upload",
      clientId,
      "install-003",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-003",
        "x-bpp-battle-id": "battle-reused-nonce",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-nonce": nonce,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 409);
  assert.deepEqual(await response.json(), { error: "nonce_reused" });
});

test("rejects replay uploads with an out-of-range timestamp", async () => {
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
      "/replays/upload",
      clientId,
      "install-004",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-004",
        "x-bpp-battle-id": "battle-old-timestamp",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-nonce": nonce,
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

test("rejects replay uploads with an invalid signature", async () => {
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
      "/replays/upload",
      clientId,
      "install-005",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-005",
        "x-bpp-battle-id": "battle-invalid-signature",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-nonce": nonce,
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
