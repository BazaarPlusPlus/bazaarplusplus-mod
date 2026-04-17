import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import { countRows, insertV3User, resetTestState } from "./helpers/seed";

beforeEach(async () => {
  await resetTestState(env);
});

test("activate creates user and bearer token atomically", async () => {
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

  expect(response.status).toBe(200);
  const json = (await response.json()) as {
    token: string;
    player_account_id: string;
    player_username: string;
  };
  expect(json.token).toMatch(/^[A-Za-z0-9_-]{43}$/);
  expect(json.player_account_id).toBe("player-account-001");
  expect(json.player_username).toBe("player-one");

  const user = await env.DB.prepare(
    `
      SELECT player_account_id, player_username
      FROM users
      WHERE player_account_id = ?
    `,
  )
    .bind("player-account-001")
    .first<{ player_account_id: string; player_username: string }>();
  expect(user).toBeTruthy();
  expect(user!.player_account_id).toBe("player-account-001");
  expect(user!.player_username).toBe("player-one");

  expect(await countRows(env.DB, "tokens")).toBe(1);
  const tokenRow = await env.DB.prepare(
    `
      SELECT player_account_id, revoked_at_utc
      FROM tokens
      WHERE token = ?
    `,
  )
    .bind(json.token)
    .first<{ player_account_id: string; revoked_at_utc: string | null }>();
  expect(tokenRow).toBeTruthy();
  expect(tokenRow!.player_account_id).toBe("player-account-001");
  expect(tokenRow!.revoked_at_utc).toBeNull();
});

test("activate rejects already-taken player_account_id", async () => {
  await insertV3User(env.DB, {
    playerAccountId: "player-account-claimed",
    playerUsername: "claimed-user",
    passwordHash: "existing-hash",
    createdAtUtc: "2026-04-10T00:00:00.000Z",
    updatedAtUtc: "2026-04-10T00:00:00.000Z",
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

  expect(response.status).toBe(409);
  expect(await response.json()).toEqual({ error: "player_account_id_taken" });
  expect(await countRows(env.DB, "users")).toBe(1);
  expect(await countRows(env.DB, "tokens")).toBe(0);
});

test("activate rejects already-taken player_username", async () => {
  await insertV3User(env.DB, {
    playerAccountId: "player-account-001",
    playerUsername: "claimed-user",
    passwordHash: "existing-hash",
    createdAtUtc: "2026-04-10T00:00:00.000Z",
    updatedAtUtc: "2026-04-10T00:00:00.000Z",
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

  expect(response.status).toBe(409);
  expect(await response.json()).toEqual({ error: "player_username_taken" });
  expect(await countRows(env.DB, "users")).toBe(1);
  expect(await countRows(env.DB, "tokens")).toBe(0);
});
