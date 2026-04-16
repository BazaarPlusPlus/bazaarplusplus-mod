import assert from "node:assert/strict";
import test from "node:test";

import { hashPassword } from "../src/crypto/password";
import worker from "../src/index";
import { buildEnv } from "./helpers/mockEnv";

test("login returns bearer token for matching password", async () => {
  const env = buildEnv();
  env.DB.v3Users.set("p1", {
    player_account_id: "p1",
    player_username: "u1",
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
        player_username: "u1",
        password: "hunter2",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const body = (await response.json()) as {
    token: string;
    player_account_id: string;
    player_username: string;
  };
  assert.match(body.token, /^[A-Za-z0-9_-]{43}$/);
  assert.equal(body.player_account_id, "p1");
  assert.equal(body.player_username, "u1");

  const tokenRow = await env.DB.prepare(
    "SELECT player_account_id, revoked_at_utc FROM tokens WHERE token = ?",
  )
    .bind(body.token)
    .first<{ player_account_id: string; revoked_at_utc: string | null }>();
  assert.ok(tokenRow);
  assert.equal(tokenRow.player_account_id, "p1");
  assert.equal(tokenRow.revoked_at_utc, null);
  assert.ok(env.DB.v3Users.get("p1")?.last_login_at_utc);
});

test("login rejects invalid password", async () => {
  const env = buildEnv();
  env.DB.v3Users.set("p1", {
    player_account_id: "p1",
    player_username: "u1",
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
        player_username: "u1",
        password: "wrong-password",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "invalid_credentials" });
  assert.equal(env.DB.v3Tokens.size, 0);
  assert.equal(env.DB.v3Users.get("p1")?.last_login_at_utc, null);
});
