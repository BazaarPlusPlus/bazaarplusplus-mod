import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import {
  insertReplayToken,
  insertRunBundle,
  insertToken,
  insertV3Battle,
  insertV3User,
  resetTestState,
  selectFirst,
} from "./helpers/seed";

const JsonEncoder = new TextEncoder();

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

async function insertReplayArtifact(args: {
  battleId: string;
  bundleId: string;
  runId: string;
  objectKey: string;
  opponentAccountId: string;
  requestedByPlayerAccountId?: string;
  bodyText?: string;
}): Promise<void> {
  const nowUtc = new Date().toISOString();

  await insertV3Battle(env.DB, {
    battleId: args.battleId,
    runId: args.runId,
    installationId: "inst_replay",
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
    installationId: "inst_replay",
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

test("replay-link returns 401 when Authorization header is missing", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-no-auth/replay-link", {
      method: "POST",
    }),
    env as never,
  );

  expect(response.status).toBe(401);
  expect(await response.json()).toEqual({ error: "invalid_token" });
});

test("replay-link returns 401 when bearer is unknown or revoked", async () => {
  await insertUserToken("tok-revoked", "player-account-001");
  await env.DB.prepare(
    "UPDATE tokens SET revoked_at_utc = ? WHERE token = ?",
  )
    .bind("2026-01-02T00:00:00Z", "tok-revoked")
    .run();

  const unknownResponse = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-foreign/replay-link", {
      method: "POST",
      headers: { Authorization: "Bearer tok-unknown" },
    }),
    env as never,
  );

  expect(unknownResponse.status).toBe(401);
  expect(await unknownResponse.json()).toEqual({ error: "invalid_token" });

  const revokedResponse = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-foreign/replay-link", {
      method: "POST",
      headers: { Authorization: "Bearer tok-revoked" },
    }),
    env as never,
  );

  expect(revokedResponse.status).toBe(401);
  expect(await revokedResponse.json()).toEqual({ error: "invalid_token" });
});

test("replay-link returns 403 when bearer is not the battle opponent", async () => {
  await insertUserToken("tok-player-001", "player-account-001");
  await insertV3Battle(env.DB, {
    battleId: "battle-foreign",
    runId: "run-foreign",
    installationId: "inst_replay",
    playerAccountId: "player-account-001",
    bundleId: "bundle-foreign",
    recordedAtUtc: new Date().toISOString(),
    day: 7,
    playerName: "Local",
    playerAccountIdInPayload: "player-account-001",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1500,
    playerLevel: 10,
    opponentName: "Remote",
    opponentAccountId: "other-player",
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1510,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-foreign/replay-link", {
      method: "POST",
      headers: { Authorization: "Bearer tok-player-001" },
    }),
    env as never,
  );

  expect(response.status).toBe(403);
  expect(await response.json()).toEqual({ error: "replay_forbidden" });
});

test("replay-link returns download metadata when bearer is the battle opponent", async () => {
  await insertUserToken("tok-player-001", "player-account-001");
  await insertV3Battle(env.DB, {
    battleId: "battle-owned",
    runId: "run-owned",
    installationId: "inst_replay",
    playerAccountId: "other-player",
    bundleId: "bundle-owned",
    recordedAtUtc: new Date().toISOString(),
    day: 8,
    playerName: "Remote",
    playerAccountIdInPayload: "other-player",
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
    new Request("https://example.com/ghost-battles/battle-owned/replay-link", {
      method: "POST",
      headers: { Authorization: "Bearer tok-player-001" },
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  const payload = (await response.json()) as {
    download_url: string;
    expires_at_utc: string;
  };
  expect(Object.keys(payload).sort()).toEqual(["download_url", "expires_at_utc"]);
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
  expect(replayToken?.expires_at_utc).toBe(payload.expires_at_utc);
});

test("replay-link allows unauthenticated creation when configured", async () => {
  env.ALLOW_UNAUTHENTICATED_REPLAY_LINKS = "true";
  await insertV3Battle(env.DB, {
    battleId: "battle-public-link",
    runId: "run-public-link",
    installationId: "inst_remote",
    playerAccountId: "remote-player",
    bundleId: "bundle-public-link",
    recordedAtUtc: new Date().toISOString(),
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
  expect(Object.keys(payload).sort()).toEqual(["download_url", "expires_at_utc"]);
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
  expect(replayToken?.expires_at_utc).toBe(payload.expires_at_utc);
});

test("download replay accepts valid short-lived token", async () => {
  await insertUserToken("tok-player-001", "player-account-001");
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
    objectKey: "run-bundles/remote-player/inst_replay/run-owned/payload-hash.mpack.gz",
    opponentAccountId: "player-account-001",
    bodyText: '{"battle_id":"battle-owned"}',
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-valid", {
      method: "GET",
      headers: {
        Authorization: "Bearer tok-player-001",
      },
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  expect(await response.text()).toBe('{"battle_id":"battle-owned"}');
});

test("download replay returns artifact_expired when artifact is no longer available", async () => {
  await insertUserToken("tok-player-001", "player-account-001");
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
    objectKey:
      "run-bundles/remote-player/inst_replay/run-expired/payload-hash-expired.mpack.gz",
    opponentAccountId: "player-account-001",
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-expired-artifact", {
      method: "GET",
      headers: {
        Authorization: "Bearer tok-player-001",
      },
    }),
    env as never,
  );

  expect(response.status).toBe(410);
  expect(await response.json()).toEqual({ error: "artifact_expired" });
});

test("download replay rejects a token created for another player", async () => {
  await insertUserToken("tok-player-001", "player-account-001");
  await insertReplayToken(env.DB, {
    token: "token-foreign",
    battleId: "battle-owned",
    requestedByPlayerAccountId: "other-player",
    expiresAtUtc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    createdAtUtc: new Date().toISOString(),
  });
  await insertV3Battle(env.DB, {
    battleId: "battle-owned",
    runId: "run-owned",
    installationId: "inst_replay",
    playerAccountId: "remote-player",
    bundleId: "bundle-owned",
    recordedAtUtc: new Date().toISOString(),
    day: 9,
    playerName: "Remote",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1600,
    playerLevel: 10,
    opponentName: "Local",
    opponentAccountId: "player-account-001",
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1610,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-foreign", {
      method: "GET",
      headers: {
        Authorization: "Bearer tok-player-001",
      },
    }),
    env as never,
  );

  expect(response.status).toBe(403);
  expect(await response.json()).toEqual({ error: "replay_token_forbidden" });
});

test("download replay allows bearer-token access when unauthenticated downloads are enabled", async () => {
  env.ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS = "true";
  await insertReplayToken(env.DB, {
    token: "token-public",
    battleId: "battle-public",
    requestedByPlayerAccountId: "other-player",
    expiresAtUtc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    createdAtUtc: new Date().toISOString(),
  });
  await insertReplayArtifact({
    battleId: "battle-public",
    bundleId: "bundle-public",
    runId: "run-public",
    objectKey:
      "run-bundles/remote-player/inst_replay/run-public/payload-hash-public.mpack.gz",
    opponentAccountId: "player-account-001",
    bodyText: '{"battle_id":"battle-public"}',
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-public", {
      method: "GET",
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  expect(await response.text()).toBe('{"battle_id":"battle-public"}');
});

test("download replay accepts bearer requests without installation headers", async () => {
  await insertUserToken("tok-unsigned", "player-account-unsigned");
  await insertReplayToken(env.DB, {
    token: "token-unsigned",
    battleId: "battle-owned-unsigned",
    requestedByPlayerAccountId: "player-account-unsigned",
    expiresAtUtc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    createdAtUtc: new Date().toISOString(),
  });
  await insertReplayArtifact({
    battleId: "battle-owned-unsigned",
    bundleId: "bundle-owned-unsigned",
    runId: "run-owned-unsigned",
    objectKey:
      "run-bundles/remote-player/inst_replay/run-owned-unsigned/payload-hash-unsigned.mpack.gz",
    opponentAccountId: "player-account-unsigned",
    bodyText: '{"battle_id":"battle-owned-unsigned"}',
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-unsigned", {
      method: "GET",
      headers: {
        Authorization: "Bearer tok-unsigned",
      },
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  expect(await response.text()).toBe('{"battle_id":"battle-owned-unsigned"}');
});
