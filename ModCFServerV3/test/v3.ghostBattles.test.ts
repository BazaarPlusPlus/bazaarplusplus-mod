import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import { buildEnv } from "./helpers/mockEnv";

test("ghost-battles ignores caller days and uses the server lookback window", async () => {
  const env = buildEnv();

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

  const query = "player_account_id=player-account-001&days=99&limit=200";

  const response = await worker.fetch(
    new Request(`https://example.com/ghost-battles?${query}`, {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  assert.deepEqual(json.battles.map((battle) => battle.battle_id), ["battle-recent"]);
});

test("ghost-battles honors the caller limit parameter after server clamping", async () => {
  const env = buildEnv();

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

  const query = "player_account_id=player-account-001&limit=1";

  const response = await worker.fetch(
    new Request(`https://example.com/ghost-battles?${query}`, {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  assert.deepEqual(json.battles.map((battle) => battle.battle_id), ["battle-003"]);
});

test("ghost-battles reads player_account_id from query", async () => {
  const env = buildEnv();

  env.DB.v3Battles.set("battle-unsigned", {
    battle_id: "battle-unsigned",
    run_id: "run-unsigned",
    installation_id: "inst_unsigned",
    player_account_id: "remote-player",
    bundle_id: "bundle-unsigned",
    recorded_at_utc: new Date().toISOString(),
    day: 8,
    player_name: "RemoteUnsigned",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "LocalUnsigned",
    opponent_account_id: "player-account-unsigned",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?player_account_id=player-account-unsigned&limit=5", {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  assert.deepEqual(json.battles.map((battle) => battle.battle_id), ["battle-unsigned"]);
});

test("ghost-battles requires player_account_id query parameter", async () => {
  const env = buildEnv();

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=5", {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "player_account_id_required" });
});
