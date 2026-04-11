import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import { buildEnv } from "./helpers/mockEnv";

test("activate creates user and first installation atomically", async () => {
  const env = buildEnv();

  const response = await worker.fetch(
    new Request("https://example.com/activate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        player_account_id: "player-account-001",
        player_username: "player-one",
        password: "hunter2",
        installation_public_key: "public-key-001",
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
  assert.equal(env.DB.v3Users.size, 1);
  assert.equal(env.DB.v3Installations.size, 1);
});

test("activate persists stream profile fields when provided", async () => {
  const env = buildEnv();

  const response = await worker.fetch(
    new Request("https://example.com/activate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        player_account_id: "player-account-stream",
        player_username: "stream-player",
        password: "hunter2",
        stream_platform: "bilibili",
        stream_channel_id: "123456",
        stream_url: "https://live.bilibili.com/123456",
        installation_public_key: "public-key-stream",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const user = env.DB.v3Users.get("player-account-stream");
  assert.ok(user);
  assert.equal(user.stream_platform, "bilibili");
  assert.equal(user.stream_channel_id, "123456");
  assert.equal(user.stream_url, "https://live.bilibili.com/123456");
});

test("activate rejects incomplete stream profile payloads", async () => {
  const env = buildEnv();

  const response = await worker.fetch(
    new Request("https://example.com/activate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        player_account_id: "player-account-bad-stream",
        player_username: "bad-stream-player",
        password: "hunter2",
        stream_platform: "bilibili",
        installation_public_key: "public-key-bad-stream",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "invalid_activate_request" });
  assert.equal(env.DB.v3Users.size, 0);
  assert.equal(env.DB.v3Installations.size, 0);
});

test("activate rejects already-claimed player_account_id", async () => {
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
        installation_public_key: "public-key-002",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 409);
  assert.deepEqual(await response.json(), { error: "player_account_id_claimed" });
  assert.equal(env.DB.v3Users.size, 1);
  assert.equal(env.DB.v3Installations.size, 0);
});

test("activate rejects already-claimed player_username", async () => {
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
        installation_public_key: "public-key-002",
      }),
    }),
    env as never,
  );

  assert.equal(response.status, 409);
  assert.deepEqual(await response.json(), { error: "player_username_claimed" });
  assert.equal(env.DB.v3Users.size, 1);
  assert.equal(env.DB.v3Installations.size, 0);
});
