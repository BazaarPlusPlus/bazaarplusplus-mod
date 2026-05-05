import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import { insertV3Battle, resetTestState } from "./helpers/seed";

beforeEach(async () => {
  await resetTestState(env);
});

async function insertOpponentBattle(
  battleId: string,
  opponentAccountId: string,
  recordedAtUtc: string,
  overrides: Partial<Parameters<typeof insertV3Battle>[1]> = {},
): Promise<void> {
  await insertV3Battle(env.DB, {
    battleId,
    runId: `run-${battleId}`,
    playerAccountId: "remote-player",
    bundleId: `bundle-${battleId}`,
    recordedAtUtc,
    day: 8,
    playerName: "Remote",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1500,
    playerLevel: 10,
    opponentName: "Local",
    opponentAccountId,
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1510,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
    ...overrides,
  });
}

test("ghost-battles returns 400 when player_account_id is missing", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=5", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(400);
  expect(await response.json()).toEqual({ error: "invalid_request" });
});

test("ghost-battles returns 400 when player_account_id is whitespace", async () => {
  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=%20%20&limit=5",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(400);
  expect(await response.json()).toEqual({ error: "invalid_request" });
});

test("ghost-battles returns battles within the lookback window", async () => {
  env.GHOST_QUERY_LOOKBACK_DAYS = "6";

  await insertOpponentBattle(
    "battle-recent",
    "player-account-001",
    new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
  );
  await insertOpponentBattle(
    "battle-old",
    "player-account-001",
    new Date(Date.now() - 5 * 24 * 60 * 60 * 1000).toISOString(),
  );

  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=player-account-001&limit=200",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as { battles: Array<{ battle_id: string }> };
  expect(json.battles.map((battle) => battle.battle_id)).toEqual([
    "battle-recent",
    "battle-old",
  ]);
});

test("ghost-battles honors the caller limit parameter after server clamping", async () => {
  await insertOpponentBattle(
    "battle-003",
    "player-account-001",
    new Date(Date.now() - 1 * 60 * 60 * 1000).toISOString(),
  );
  await insertOpponentBattle(
    "battle-002",
    "player-account-001",
    new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
  );
  await insertOpponentBattle(
    "battle-001",
    "player-account-001",
    new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
  );

  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=player-account-001&limit=1",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as { battles: Array<{ battle_id: string }> };
  expect(json.battles.map((battle) => battle.battle_id)).toEqual(["battle-003"]);
});

test("ghost-battles only returns rows where opponent_account_id matches", async () => {
  await insertOpponentBattle(
    "battle-owned",
    "player-account-001",
    new Date().toISOString(),
  );
  await insertOpponentBattle(
    "battle-other",
    "someone-else",
    new Date(Date.now() - 60 * 1000).toISOString(),
  );

  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=player-account-001&limit=5",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as { battles: Array<{ battle_id: string }> };
  expect(json.battles.map((battle) => battle.battle_id)).toEqual(["battle-owned"]);
});

test("ghost-battles includes the bundle-final battle marker", async () => {
  await insertOpponentBattle(
    "battle-final-loss",
    "player-account-001",
    new Date().toISOString(),
    { result: "Lost", isBundleFinalBattle: 1 },
  );

  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=player-account-001&limit=5",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string; is_bundle_final_battle: boolean }>;
  };
  expect(json.battles).toMatchObject([
    {
      battle_id: "battle-final-loss",
      is_bundle_final_battle: true,
    },
  ]);
});
