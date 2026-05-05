import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import {
  insertReplayToken,
  insertRunBundle,
  insertV3Battle,
  resetTestState,
  selectFirst,
} from "./helpers/seed";

const JsonEncoder = new TextEncoder();

async function insertReplayArtifact(args: {
  battleId: string;
  bundleId: string;
  runId: string;
  objectKey: string;
  opponentAccountId: string;
  bodyText?: string;
}): Promise<void> {
  const nowUtc = new Date().toISOString();

  await insertV3Battle(env.DB, {
    battleId: args.battleId,
    runId: args.runId,
    playerAccountId: "remote-player",
    bundleId: args.bundleId,
    recordedAtUtc: nowUtc,
    day: 9,
    playerName: "Remote",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1600,
    playerLevel: 10,
    opponentName: "Local",
    opponentAccountId: args.opponentAccountId,
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1610,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: nowUtc,
  });
  await insertRunBundle(env.DB, {
    bundleId: args.bundleId,
    playerAccountId: "remote-player",
    runId: args.runId,
    payloadHash: `${args.bundleId}-hash`,
    schemaVersion: 3,
    objectKey: args.objectKey,
    codec: "application/json",
    sizeBytes: 12,
    submittedAtUtc: nowUtc,
    createdAtUtc: nowUtc,
  });

  if (args.bodyText != null) {
    await env.RUN_BUNDLE_BUCKET.put(
      args.objectKey,
      JsonEncoder.encode(args.bodyText),
      { httpMetadata: { contentType: "application/json" } },
    );
  }
}

beforeEach(async () => {
  await resetTestState(env);
});

test("replay-link returns 404 when battle does not exist", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/missing-battle/replay-link", {
      method: "POST",
    }),
    env as never,
  );

  expect(response.status).toBe(404);
  expect(await response.json()).toEqual({ error: "battle_not_found" });
});

test("replay-link returns 403 when battle has no opponent account id", async () => {
  await insertV3Battle(env.DB, {
    battleId: "battle-no-opponent",
    runId: "run-no-opponent",
    playerAccountId: "remote-player",
    bundleId: "bundle-no-opponent",
    recordedAtUtc: new Date().toISOString(),
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-no-opponent/replay-link", {
      method: "POST",
    }),
    env as never,
  );

  expect(response.status).toBe(403);
  expect(await response.json()).toEqual({ error: "replay_forbidden" });
});

test("replay-link returns download metadata for any battle with an opponent", async () => {
  await insertV3Battle(env.DB, {
    battleId: "battle-public-link",
    runId: "run-public-link",
    playerAccountId: "remote-player",
    bundleId: "bundle-public-link",
    recordedAtUtc: new Date().toISOString(),
    opponentAccountId: "player-account-001",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-public-link/replay-link", {
      method: "POST",
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  const payload = (await response.json()) as {
    download_url: string;
    expires_at_utc: string;
  };
  expect(payload.download_url).toMatch(/^https:\/\/example\.com\/replays\/replay_/);
  expect(payload.expires_at_utc).toMatch(/^\d{4}-\d{2}-\d{2}T/);

  const replayToken = await selectFirst<{
    requested_by_player_account_id: string;
    expires_at_utc: string;
  }>(
    env.DB,
    `
      SELECT requested_by_player_account_id, expires_at_utc
      FROM replay_tokens
      WHERE token = ?
    `,
    payload.download_url.split("/").pop() ?? "",
  );
  expect(replayToken?.requested_by_player_account_id).toBe("player-account-001");
});

test("download replay accepts a valid short-lived token", async () => {
  await insertReplayToken(env.DB, {
    token: "token-valid",
    battleId: "battle-owned",
    requestedByPlayerAccountId: "player-account-001",
    expiresAtUtc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    createdAtUtc: new Date().toISOString(),
  });
  await insertReplayArtifact({
    battleId: "battle-owned",
    bundleId: "bundle-owned",
    runId: "run-owned",
    objectKey: "run-bundles/remote-player/run-owned/payload-hash.mpack.gz",
    opponentAccountId: "player-account-001",
    bodyText: '{"battle_id":"battle-owned"}',
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-valid", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(200);
  expect(await response.text()).toBe('{"battle_id":"battle-owned"}');
});

test("download replay returns artifact_expired when artifact is missing", async () => {
  await insertReplayToken(env.DB, {
    token: "token-expired-artifact",
    battleId: "battle-expired",
    requestedByPlayerAccountId: "player-account-001",
    expiresAtUtc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    createdAtUtc: new Date().toISOString(),
  });
  await insertReplayArtifact({
    battleId: "battle-expired",
    bundleId: "bundle-expired",
    runId: "run-expired",
    objectKey: "run-bundles/remote-player/run-expired/payload-hash-expired.mpack.gz",
    opponentAccountId: "player-account-001",
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-expired-artifact", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(410);
  expect(await response.json()).toEqual({ error: "artifact_expired" });
});

test("download replay returns 410 when token has expired", async () => {
  await insertReplayToken(env.DB, {
    token: "token-too-old",
    battleId: "battle-irrelevant",
    requestedByPlayerAccountId: "player-account-001",
    expiresAtUtc: new Date(Date.now() - 60 * 1000).toISOString(),
    createdAtUtc: new Date(Date.now() - 10 * 60 * 1000).toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-too-old", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(410);
  expect(await response.json()).toEqual({ error: "replay_token_expired" });
});

test("download replay returns 404 when token has been revoked", async () => {
  await insertReplayToken(env.DB, {
    token: "token-revoked",
    battleId: "battle-irrelevant",
    requestedByPlayerAccountId: "player-account-001",
    expiresAtUtc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    createdAtUtc: new Date().toISOString(),
    revokedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-revoked", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(404);
  expect(await response.json()).toEqual({ error: "replay_token_not_found" });
});
