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

test("binds a signed client to a player account and records an observed account", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-bind-001";
  const installId = "install-bind-001";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: installId,
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    player_account_id: "player-account-001",
    observed_player_account_id: "player-account-001",
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-bind-001";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/clients/bind",
      clientId,
      installId,
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/clients/bind", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": installId,
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
  assert.deepEqual(await response.json(), {
    status: "bound",
    player_account_id: "player-account-001",
  });

  const binding = Array.from(env.DB.clientPlayerAccountBindings.values()).find(
    (row) => row.client_id === clientId && row.unbound_at_utc == null,
  );
  assert.equal(binding?.player_account_id, "player-account-001");
  assert.equal(binding?.binding_source, "client_bind");

  const observation = env.DB.clientPlayerAccountObservations.get(
    `${clientId}:player-account-001`,
  );
  assert.equal(observation?.player_account_id, "player-account-001");
  assert.equal(observation?.client_id, clientId);
});
