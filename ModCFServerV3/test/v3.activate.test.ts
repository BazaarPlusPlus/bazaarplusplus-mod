import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import { buildEnv } from "./helpers/mockEnv";

test("activate creates user and bearer token atomically", async () => {
  const env = buildEnv();

  const response = await worker.fetch(
    new Request("https://example.com/activate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        player_account_id: "player-account-001",
        player_username: "player-one",
        password: "hunter2",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    token: string;
    player_account_id: string;
    player_username: string;
  };
  assert.match(json.token, /^[A-Za-z0-9_-]{43}$/);
  assert.equal(json.player_account_id, "player-account-001");
  assert.equal(json.player_username, "player-one");

  const user = env.DB.v3Users.get("player-account-001");
  assert.ok(user);
  assert.equal(user.player_account_id, "player-account-001");
  assert.equal(user.player_username, "player-one");

  assert.equal(env.DB.v3Tokens.size, 1);
  const tokenRow = env.DB.v3Tokens.get(json.token);
  assert.ok(tokenRow);
  assert.equal(tokenRow.player_account_id, "player-account-001");
  assert.equal(tokenRow.revoked_at_utc, null);
});

test("activate rejects already-taken player_account_id", async () => {
  const env = buildEnv();
  env.DB.v3Users.set("player-account-claimed", {
    player_account_id: "player-account-claimed",
    player_username: "claimed-user",
    password_hash: "existing-hash",
    stream_platform: null,
    stream_channel_id: null,
    stream_url: null,
    created_at_utc: "2026-04-10T00:00:00.000Z",
    updated_at_utc: "2026-04-10T00:00:00.000Z",
    last_login_at_utc: null,
  });

  const response = await worker.fetch(
    new Request("https://example.com/activate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        player_account_id: "player-account-claimed",
        player_username: "other-user",
        password: "hunter2",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 409);
  assert.deepEqual(await response.json(), { error: "player_account_id_taken" });
  assert.equal(env.DB.v3Users.size, 1);
  assert.equal(env.DB.v3Tokens.size, 0);
});

test("activate rejects already-taken player_username", async () => {
  const env = buildEnv();
  env.DB.v3Users.set("player-account-001", {
    player_account_id: "player-account-001",
    player_username: "claimed-user",
    password_hash: "existing-hash",
    stream_platform: null,
    stream_channel_id: null,
    stream_url: null,
    created_at_utc: "2026-04-10T00:00:00.000Z",
    updated_at_utc: "2026-04-10T00:00:00.000Z",
    last_login_at_utc: null,
  });

  const response = await worker.fetch(
    new Request("https://example.com/activate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        player_account_id: "player-account-002",
        player_username: "claimed-user",
        password: "hunter2",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 409);
  assert.deepEqual(await response.json(), { error: "player_username_taken" });
  assert.equal(env.DB.v3Users.size, 1);
  assert.equal(env.DB.v3Tokens.size, 0);
});
