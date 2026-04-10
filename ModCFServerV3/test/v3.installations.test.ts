import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import { buildEnv } from "./helpers/mockEnv";

test("installations creates a new installation for the logged-in user", async () => {
  const env = buildEnv();
  env.DB.v3InstallationSessions.set("sess_valid", {
    session_id: "sess_valid",
    player_account_id: "player-account-001",
    created_at_utc: "2026-04-11T00:00:00.000Z",
    expires_at_utc: "2099-04-11T00:30:00.000Z",
    revoked_at_utc: null,
  });

  const response = await worker.fetch(
    new Request("https://example.com/installations", {
      method: "POST",
      headers: {
        authorization: "Bearer sess_valid",
        "content-type": "application/json",
      },
      body: JSON.stringify({
        player_account_id: "player-account-001",
        installation_public_key: '{"modulus_b64":"abc","exponent_b64":"AQAB"}',
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    installation_id: string;
    status: string;
  };
  assert.match(json.installation_id, /^inst_/);
  assert.equal(json.status, "active");
  assert.equal(env.DB.v3Installations.size, 1);
});

test("installations rejects mismatched observed player account id", async () => {
  const env = buildEnv();
  env.DB.v3InstallationSessions.set("sess_valid", {
    session_id: "sess_valid",
    player_account_id: "player-account-001",
    created_at_utc: "2026-04-11T00:00:00.000Z",
    expires_at_utc: "2099-04-11T00:30:00.000Z",
    revoked_at_utc: null,
  });

  const response = await worker.fetch(
    new Request("https://example.com/installations", {
      method: "POST",
      headers: {
        authorization: "Bearer sess_valid",
        "content-type": "application/json",
      },
      body: JSON.stringify({
        player_account_id: "player-account-999",
        installation_public_key: '{"modulus_b64":"abc","exponent_b64":"AQAB"}',
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 403);
  assert.deepEqual(await response.json(), { error: "player_account_mismatch" });
  assert.equal(env.DB.v3Installations.size, 0);
});
