import assert from "node:assert/strict";
import test from "node:test";

import { hashPassword } from "../src/crypto/password";
import worker from "../src/index";
import { buildEnv } from "./helpers/mockEnv";

test("login returns installer session for matching password", async () => {
  const env = buildEnv();
  env.DB.v3Users.set("player-account-001", {
    player_account_id: "player-account-001",
    player_username: "player-one",
    password_hash: await hashPassword("hunter2"),
    stream_platform: null,
    stream_channel_id: null,
    stream_url: null,
    created_at_utc: "2026-04-10T00:00:00.000Z",
    updated_at_utc: "2026-04-10T00:00:00.000Z",
    last_login_at_utc: null,
  });

  const response = await worker.fetch(
    new Request("https://example.com/login", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        player_username: "player-one",
        password: "hunter2",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    session_token: string;
  };
  assert.match(json.session_token, /^sess_/);
  assert.equal(env.DB.v3InstallationSessions.size, 1);
  assert.ok(env.DB.v3Users.get("player-account-001")?.last_login_at_utc);
});

test("login rejects invalid password", async () => {
  const env = buildEnv();
  env.DB.v3Users.set("player-account-001", {
    player_account_id: "player-account-001",
    player_username: "player-one",
    password_hash: await hashPassword("hunter2"),
    stream_platform: null,
    stream_channel_id: null,
    stream_url: null,
    created_at_utc: "2026-04-10T00:00:00.000Z",
    updated_at_utc: "2026-04-10T00:00:00.000Z",
    last_login_at_utc: null,
  });

  const response = await worker.fetch(
    new Request("https://example.com/login", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        player_username: "player-one",
        password: "wrong-password",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "invalid_credentials" });
  assert.equal(env.DB.v3InstallationSessions.size, 0);
  assert.equal(env.DB.v3Users.get("player-account-001")?.last_login_at_utc, null);
});

