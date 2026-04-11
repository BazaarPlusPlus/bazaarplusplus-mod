import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import {
  canonicalRequestV3,
  generateClientKeyPair,
  sha256Base64,
  signCanonical,
} from "./helpers/crypto";
import { buildEnv } from "./helpers/mockEnv";

test("installation-signed observation is accepted once", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_001", {
    installation_id: "inst_001",
    player_account_id: "player-account-001",
    public_key: JSON.stringify({
      modulus_b64: modulusB64,
      exponent_b64: exponentB64,
    }),
    status: "active",
    created_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });

  const body = JSON.stringify({
    observed_player_account_id: "player-account-001",
    observed_player_username: "player-one",
    observed_at_utc: "2026-04-10T00:00:00.000Z",
  });
  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64(body);
  const signature = signCanonical(
    privateKey,
    canonicalRequestV3({
      method: "POST",
      path: "/installations/observations",
      query: "",
      installationId: "inst_001",
      timestamp,
      bodyHash,
    }),
  );

  const response = await worker.fetch(
    new Request("https://example.com/installations/observations", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-installation-id": "inst_001",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature": signature,
      },
      body,
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { status: "accepted" });
  assert.equal(env.DB.v3InstallationObservations.size, 1);
});

test("unsigned observation is accepted when installation id is provided", async () => {
  const env = buildEnv();
  env.DB.v3Installations.set("inst_unsigned", {
    installation_id: "inst_unsigned",
    player_account_id: "player-account-unsigned",
    public_key: JSON.stringify({
      modulus_b64: "unused",
      exponent_b64: "unused",
    }),
    status: "active",
    created_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });

  const body = JSON.stringify({
    observed_player_account_id: "player-account-unsigned",
    observed_player_username: "player-unsigned",
    observed_at_utc: "2026-04-10T00:00:00.000Z",
  });

  const response = await worker.fetch(
    new Request("https://example.com/installations/observations", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-installation-id": "inst_unsigned",
      },
      body,
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { status: "accepted" });
  assert.equal(env.DB.v3InstallationObservations.size, 1);
});
