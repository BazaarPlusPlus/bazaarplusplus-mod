import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import {
  canonicalRequest,
  generateClientKeyPair,
  sha256Base64,
  signCanonical,
} from "./helpers/crypto";
import { buildEnv } from "./helpers/mockEnv";

test("accepts signed run uploads", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-001";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-001",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ run_id: "run-001", state: "active" });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-run-001";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-001",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/runs/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-run-001",
        "x-bpp-run-id": "run-001",
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

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { status: "accepted" });
  assert.equal(env.DB.runUploads.get("run-001")?.client_id, clientId);
});

test("re-uploading the same run payload remains idempotent", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-repeat";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-repeat",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ run_id: "run-repeat", state: "active" });
  const bodyHash = sha256Base64(payload);

  for (const nonce of ["nonce-run-repeat-1", "nonce-run-repeat-2"]) {
    const timestamp = new Date().toISOString();
    const signature = signCanonical(
      privateKey,
      canonicalRequest(
        "POST",
        "/runs/upload",
        clientId,
        "install-run-repeat",
        timestamp,
        nonce,
        bodyHash,
      ),
    );

    const response = await worker.fetch(
      new Request("https://example.com/runs/upload", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "x-bpp-client-id": clientId,
          "x-bpp-install-id": "install-run-repeat",
          "x-bpp-run-id": "run-repeat",
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

    assert.equal(response.status, 200);
  }

  assert.equal(env.DB.runUploads.size, 1);
});
