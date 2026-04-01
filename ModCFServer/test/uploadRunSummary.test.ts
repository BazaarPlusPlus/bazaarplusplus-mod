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

test("stores signed run summaries in D1 and R2", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-run-001";
  const installId = "install-run-001";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: installId,
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });

  const payload = JSON.stringify({
    run_id: "run-001",
    status: "won",
    hero_id: "hero-vanessa",
    hero_name: "Vanessa",
    ended_at_utc: "2026-04-01T00:00:00.000Z",
    final_day: 10,
    final_wins: 10,
    final_losses: 2,
    mmr: 1820,
    schema_version: 1,
  });
  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64(payload);
  const signature = signCanonical(
    privateKey,
    canonicalRequest("POST", "/runs", clientId, installId, timestamp, bodyHash),
  );

  const response = await worker.fetch(
    new Request("https://example.com/runs", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": installId,
        "x-bpp-timestamp": timestamp,
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

  const run = env.DB.runs.get("run-001");
  assert.equal(run?.client_id, clientId);
  assert.equal(run?.status, "won");
  assert.equal(run?.hero_id, "hero-vanessa");
  assert.equal(run?.summary_schema_version, 1);
  assert.ok(run?.summary_object_key);
  assert.ok(env.PVP_BATTLE_BUCKET.objects.has(run!.summary_object_key!));
});
