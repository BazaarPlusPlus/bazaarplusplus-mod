import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import {
  insertToken,
  insertV3Battle,
  insertV3User,
  resetTestState,
} from "./helpers/seed";

async function insertUserToken(
  token: string,
  playerAccountId: string,
): Promise<void> {
  await insertV3User(env.DB, {
    playerAccountId,
    playerUsername: `${playerAccountId}-user`,
    passwordHash: "hash",
    createdAtUtc: "2026-01-01T00:00:00Z",
    updatedAtUtc: "2026-01-01T00:00:00Z",
  });
  await insertToken(env.DB, {
    token,
    playerAccountId,
    issuedAtUtc: "2026-01-01T00:00:00Z",
  });
}

beforeEach(async () => {
  await resetTestState(env);
});

test("ghost-battles returns 401 when Authorization header is missing", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=5", {
      method: "GET",
    }),
    env as never,
  );

  expect(response.status).toBe(401);
  expect(await response.json()).toEqual({ error: "invalid_token" });
});

test("ghost-battles returns 401 when bearer is unknown or revoked", async () => {
  await insertV3User(env.DB, {
    playerAccountId: "player-account-001",
    playerUsername: "player-001",
    passwordHash: "hash",
    createdAtUtc: "2026-01-01T00:00:00Z",
    updatedAtUtc: "2026-01-01T00:00:00Z",
  });
  await insertToken(env.DB, {
    token: "tok-revoked",
    playerAccountId: "player-account-001",
    issuedAtUtc: "2026-01-01T00:00:00Z",
    revokedAtUtc: "2026-01-02T00:00:00Z",
  });

  const unknownResponse = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=5", {
      method: "GET",
      headers: { Authorization: "Bearer tok-unknown" },
    }),
    env as never,
  );

  expect(unknownResponse.status).toBe(401);
  expect(await unknownResponse.json()).toEqual({ error: "invalid_token" });

  const revokedResponse = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=5", {
      method: "GET",
      headers: { Authorization: "Bearer tok-revoked" },
    }),
    env as never,
  );

  expect(revokedResponse.status).toBe(401);
  expect(await revokedResponse.json()).toEqual({ error: "invalid_token" });
});

test("ghost-battles returns battles for the authenticated bearer and ignores caller days", async () => {
  env.GHOST_QUERY_LOOKBACK_DAYS = "6";
  await insertUserToken("tok-player-001", "player-account-001");

  await insertV3Battle(env.DB, {
    battleId: "battle-recent",
    runId: "run-001",
    installationId: "inst_ghost",
    playerAccountId: "remote-player",
    bundleId: "bundle-001",
    recordedAtUtc: new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
    day: 8,
    playerName: "Remote",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1500,
    playerLevel: 10,
    opponentName: "Local",
    opponentAccountId: "player-account-001",
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1510,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });
  await insertV3Battle(env.DB, {
    battleId: "battle-old",
    runId: "run-002",
    installationId: "inst_ghost",
    playerAccountId: "remote-player",
    bundleId: "bundle-002",
    recordedAtUtc: new Date(Date.now() - 5 * 24 * 60 * 60 * 1000).toISOString(),
    day: 3,
    playerName: "RemoteOld",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Silver",
    playerRating: 1200,
    playerLevel: 7,
    opponentName: "Local",
    opponentAccountId: "player-account-001",
    opponentHero: "HeroB",
    opponentRank: "Silver",
    opponentRating: 1210,
    opponentLevel: 8,
    result: "Lost",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?days=99&limit=200", {
      method: "GET",
      headers: { Authorization: "Bearer tok-player-001" },
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  expect(json.battles.map((battle) => battle.battle_id)).toEqual([
    "battle-recent",
    "battle-old",
  ]);
});

test("ghost-battles honors the caller limit parameter after server clamping", async () => {
  await insertUserToken("tok-player-001", "player-account-001");

  await insertV3Battle(env.DB, {
    battleId: "battle-003",
    runId: "run-003",
    installationId: "inst_ghost",
    playerAccountId: "remote-player",
    bundleId: "bundle-003",
    recordedAtUtc: new Date(Date.now() - 1 * 60 * 60 * 1000).toISOString(),
    day: 8,
    playerName: "RemoteNewest",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1500,
    playerLevel: 10,
    opponentName: "Local",
    opponentAccountId: "player-account-001",
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1510,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });
  await insertV3Battle(env.DB, {
    battleId: "battle-002",
    runId: "run-002",
    installationId: "inst_ghost",
    playerAccountId: "remote-player",
    bundleId: "bundle-002",
    recordedAtUtc: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
    day: 8,
    playerName: "RemoteMiddle",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1500,
    playerLevel: 10,
    opponentName: "Local",
    opponentAccountId: "player-account-001",
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1510,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });
  await insertV3Battle(env.DB, {
    battleId: "battle-001",
    runId: "run-001",
    installationId: "inst_ghost",
    playerAccountId: "remote-player",
    bundleId: "bundle-001",
    recordedAtUtc: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
    day: 8,
    playerName: "RemoteOldest",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1500,
    playerLevel: 10,
    opponentName: "Local",
    opponentAccountId: "player-account-001",
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1510,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=1", {
      method: "GET",
      headers: { Authorization: "Bearer tok-player-001" },
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  expect(json.battles.map((battle) => battle.battle_id)).toEqual(["battle-003"]);
});

test("ghost-battles ignores mismatched player_account_id query parameters when bearer is valid", async () => {
  await insertUserToken("tok-player-001", "player-account-001");

  await insertV3Battle(env.DB, {
    battleId: "battle-owned",
    runId: "run-owned",
    installationId: "inst_ghost",
    playerAccountId: "remote-player",
    bundleId: "bundle-owned",
    recordedAtUtc: new Date().toISOString(),
    day: 8,
    playerName: "RemoteOwned",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1500,
    playerLevel: 10,
    opponentName: "LocalOwned",
    opponentAccountId: "player-account-001",
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1510,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });
  await insertV3Battle(env.DB, {
    battleId: "battle-other",
    runId: "run-other",
    installationId: "inst_ghost",
    playerAccountId: "remote-player",
    bundleId: "bundle-other",
    recordedAtUtc: new Date(Date.now() - 60 * 1000).toISOString(),
    day: 8,
    playerName: "RemoteOther",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1500,
    playerLevel: 10,
    opponentName: "LocalOther",
    opponentAccountId: "someone-else",
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1510,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
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

  expect(response.status).toBe(200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  expect(json.battles.map((battle) => battle.battle_id)).toEqual(["battle-owned"]);
});
