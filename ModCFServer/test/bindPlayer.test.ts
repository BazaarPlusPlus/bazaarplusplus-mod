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

test("binds a signed client to a player account", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-bind-001";
  const installId = "install-bind-001";
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
    player_account_id: "player-account-001",
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/clients/bind-player",
      clientId,
      installId,
      timestamp,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/clients/bind-player", {
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
  assert.deepEqual(await response.json(), {
    status: "bound",
    player_account_id: "player-account-001",
  });

  const playerLink = env.DB.playerLinks.get(clientId);
  assert.equal(playerLink?.client_id, clientId);
  assert.equal(playerLink?.player_account_id, "player-account-001");
});

test("rebinding replaces the linked player account for the client", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-bind-replace";
  const installId = "install-bind-replace";
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

  for (const playerAccountId of ["player-account-001", "player-account-002"]) {
    const payload = JSON.stringify({ player_account_id: playerAccountId });
    const bodyHash = sha256Base64(payload);
    const timestamp = new Date().toISOString();
    const signature = signCanonical(
      privateKey,
      canonicalRequest(
        "POST",
        "/clients/bind-player",
        clientId,
        installId,
        timestamp,
        bodyHash,
      ),
    );

    const response = await worker.fetch(
      new Request("https://example.com/clients/bind-player", {
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
  }

  assert.equal(env.DB.playerLinks.size, 1);
  assert.equal(
    env.DB.playerLinks.get(clientId)?.player_account_id,
    "player-account-002",
  );
});
