import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import { hashPassword } from "../src/crypto/password";
import worker from "../src/index";
import { countRows, insertV3User, resetTestState } from "./helpers/seed";

beforeEach(async () => {
  await resetTestState(env);
});

test("login returns bearer token for matching password", async () => {
  await insertV3User(env.DB, {
    playerAccountId: "p1",
    playerUsername: "u1",
    passwordHash: await hashPassword("hunter2"),
    createdAtUtc: "2026-04-10T00:00:00.000Z",
    updatedAtUtc: "2026-04-10T00:00:00.000Z",
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

  expect(response.status).toBe(200);
  const body = (await response.json()) as {
    token: string;
    player_account_id: string;
    player_username: string;
  };
  expect(body.token).toMatch(/^[A-Za-z0-9_-]{43}$/);
  expect(body.player_account_id).toBe("p1");
  expect(body.player_username).toBe("u1");

  const tokenRow = await env.DB.prepare(
    "SELECT player_account_id, revoked_at_utc FROM tokens WHERE token = ?",
  )
    .bind(body.token)
    .first<{ player_account_id: string; revoked_at_utc: string | null }>();
  expect(tokenRow).toBeTruthy();
  expect(tokenRow!.player_account_id).toBe("p1");
  expect(tokenRow!.revoked_at_utc).toBeNull();
  const userRow = await env.DB.prepare(
    "SELECT last_login_at_utc FROM users WHERE player_account_id = ?",
  )
    .bind("p1")
    .first<{ last_login_at_utc: string | null }>();
  expect(userRow?.last_login_at_utc).toBeTruthy();
});

test("login rejects invalid password", async () => {
  await insertV3User(env.DB, {
    playerAccountId: "p1",
    playerUsername: "u1",
    passwordHash: await hashPassword("hunter2"),
    createdAtUtc: "2026-04-10T00:00:00.000Z",
    updatedAtUtc: "2026-04-10T00:00:00.000Z",
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

  expect(response.status).toBe(401);
  expect(await response.json()).toEqual({ error: "invalid_credentials" });
  expect(await countRows(env.DB, "tokens")).toBe(0);
  const userRow = await env.DB.prepare(
    "SELECT last_login_at_utc FROM users WHERE player_account_id = ?",
  )
    .bind("p1")
    .first<{ last_login_at_utc: string | null }>();
  expect(userRow?.last_login_at_utc).toBeNull();
});
