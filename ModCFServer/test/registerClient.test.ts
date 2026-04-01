import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import { generateClientKeyPair } from "./helpers/crypto";
import { buildEnv } from "./helpers/mockEnv";

test("registers clients without a purpose field", async () => {
  const env = buildEnv();
  const { modulusB64, exponentB64 } = generateClientKeyPair();

  const response = await worker.fetch(
    new Request("https://example.com/clients/register", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        install_id: "install-001",
        plugin_version: "1.9.0",
        public_key: {
          modulus_b64: modulusB64,
          exponent_b64: exponentB64,
        },
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    client_id: string;
    status: string;
  };
  assert.ok(json.client_id);
  assert.equal(json.status, "registered");
  assert.equal(env.DB.clients.size, 1);

  const registered = env.DB.clients.get(json.client_id);
  assert.equal(registered?.install_id, "install-001");
  assert.equal(registered?.plugin_version, "1.9.0");
});

test("re-registering the same client returns the existing client id", async () => {
  const env = buildEnv();
  const { modulusB64, exponentB64 } = generateClientKeyPair();
  const body = JSON.stringify({
    install_id: "install-repeat",
    plugin_version: "1.9.0",
    public_key: {
      modulus_b64: modulusB64,
      exponent_b64: exponentB64,
    },
  });

  const first = await worker.fetch(
    new Request("https://example.com/clients/register", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body,
    }),
    env as never,
  );
  assert.equal(first.status, 200);
  const firstJson = (await first.json()) as { client_id: string };

  const second = await worker.fetch(
    new Request("https://example.com/clients/register", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body,
    }),
    env as never,
  );
  assert.equal(second.status, 200);
  const secondJson = (await second.json()) as { client_id: string };
  assert.equal(secondJson.client_id, firstJson.client_id);
  assert.equal(env.DB.clients.size, 1);
});
