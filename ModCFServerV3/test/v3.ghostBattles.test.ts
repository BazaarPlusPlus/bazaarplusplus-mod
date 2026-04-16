import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import type { MockD1Database } from "./helpers/mockEnv";
import { buildEnv } from "./helpers/mockEnv";

async function insertToken(
  db: MockD1Database,
  token: string,
  playerAccountId: string,
  issuedAtUtc: string,
): Promise<void> {
  await db.prepare(
    `INSERT INTO tokens (token, player_account_id, issued_at_utc) VALUES (?, ?, ?)`,
  )
    .bind(token, playerAccountId, issuedAtUtc)
    .run();
}

test("ghost-battles returns 401 when Authorization header is missing", async () => {
  const env = buildEnv();

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=5", {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "invalid_token" });
});

test("ghost-battles returns 401 when bearer is unknown or revoked", async () => {
  const env = buildEnv();
  await env.DB.prepare(
    `INSERT INTO tokens (token, player_account_id, issued_at_utc, revoked_at_utc) VALUES (?, ?, ?, ?)`,
  )
    .bind("tok-revoked", "player-account-001", "2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z")
    .run();

  const unknownResponse = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=5", {
      method: "GET",
      headers: { Authorization: "Bearer tok-unknown" },
    }),
    env as never,
  );

  assert.equal(unknownResponse.status, 401);
  assert.deepEqual(await unknownResponse.json(), { error: "invalid_token" });

  const revokedResponse = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=5", {
      method: "GET",
      headers: { Authorization: "Bearer tok-revoked" },
    }),
    env as never,
  );

  assert.equal(revokedResponse.status, 401);
  assert.deepEqual(await revokedResponse.json(), { error: "invalid_token" });
});

test("ghost-battles returns battles for the authenticated bearer and ignores caller days", async () => {
  const env = buildEnv();
  env.GHOST_QUERY_LOOKBACK_DAYS = "6";
  await insertToken(
    env.DB,
    "tok-player-001",
    "player-account-001",
    "2026-01-01T00:00:00Z",
  );

  env.DB.v3Battles.set("battle-recent", {
    battle_id: "battle-recent",
    run_id: "run-001",
    installation_id: "inst_ghost",
    player_account_id: "remote-player",
    bundle_id: "bundle-001",
    recorded_at_utc: new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
    day: 8,
    player_name: "Remote",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "Local",
    opponent_account_id: "player-account-001",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });
  env.DB.v3Battles.set("battle-old", {
    battle_id: "battle-old",
    run_id: "run-002",
    installation_id: "inst_ghost",
    player_account_id: "remote-player",
    bundle_id: "bundle-002",
    recorded_at_utc: new Date(Date.now() - 5 * 24 * 60 * 60 * 1000).toISOString(),
    day: 3,
    player_name: "RemoteOld",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Silver",
    player_rating: 1200,
    player_level: 7,
    opponent_name: "Local",
    opponent_account_id: "player-account-001",
    opponent_hero: "HeroB",
    opponent_rank: "Silver",
    opponent_rating: 1210,
    opponent_level: 8,
    result: "Lost",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?days=99&limit=200", {
      method: "GET",
      headers: { Authorization: "Bearer tok-player-001" },
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  assert.deepEqual(json.battles.map((battle) => battle.battle_id), [
    "battle-recent",
    "battle-old",
  ]);
});

test("ghost-battles honors the caller limit parameter after server clamping", async () => {
  const env = buildEnv();
  await insertToken(
    env.DB,
    "tok-player-001",
    "player-account-001",
    "2026-01-01T00:00:00Z",
  );

  env.DB.v3Battles.set("battle-003", {
    battle_id: "battle-003",
    run_id: "run-003",
    installation_id: "inst_ghost",
    player_account_id: "remote-player",
    bundle_id: "bundle-003",
    recorded_at_utc: new Date(Date.now() - 1 * 60 * 60 * 1000).toISOString(),
    day: 8,
    player_name: "RemoteNewest",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "Local",
    opponent_account_id: "player-account-001",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });
  env.DB.v3Battles.set("battle-002", {
    battle_id: "battle-002",
    run_id: "run-002",
    installation_id: "inst_ghost",
    player_account_id: "remote-player",
    bundle_id: "bundle-002",
    recorded_at_utc: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
    day: 8,
    player_name: "RemoteMiddle",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "Local",
    opponent_account_id: "player-account-001",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });
  env.DB.v3Battles.set("battle-001", {
    battle_id: "battle-001",
    run_id: "run-001",
    installation_id: "inst_ghost",
    player_account_id: "remote-player",
    bundle_id: "bundle-001",
    recorded_at_utc: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
    day: 8,
    player_name: "RemoteOldest",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "Local",
    opponent_account_id: "player-account-001",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=1", {
      method: "GET",
      headers: { Authorization: "Bearer tok-player-001" },
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  assert.deepEqual(json.battles.map((battle) => battle.battle_id), ["battle-003"]);
});

test("ghost-battles ignores mismatched player_account_id query parameters when bearer is valid", async () => {
  const env = buildEnv();
  await insertToken(
    env.DB,
    "tok-player-001",
    "player-account-001",
    "2026-01-01T00:00:00Z",
  );

  env.DB.v3Battles.set("battle-owned", {
    battle_id: "battle-owned",
    run_id: "run-owned",
    installation_id: "inst_ghost",
    player_account_id: "remote-player",
    bundle_id: "bundle-owned",
    recorded_at_utc: new Date().toISOString(),
    day: 8,
    player_name: "RemoteOwned",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "LocalOwned",
    opponent_account_id: "player-account-001",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });
  env.DB.v3Battles.set("battle-other", {
    battle_id: "battle-other",
    run_id: "run-other",
    installation_id: "inst_ghost",
    player_account_id: "remote-player",
    bundle_id: "bundle-other",
    recorded_at_utc: new Date(Date.now() - 60 * 1000).toISOString(),
    day: 8,
    player_name: "RemoteOther",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "LocalOther",
    opponent_account_id: "someone-else",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=someone-else&limit=5",
      {
        method: "GET",
        headers: { Authorization: "Bearer tok-player-001" },
      },
    ),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  assert.deepEqual(json.battles.map((battle) => battle.battle_id), ["battle-owned"]);
});
