import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import { sha256Base64, toBase64UrlSegment } from "./helpers/crypto";
import { buildEnv } from "./helpers/mockEnv";

function buildUploadRequest(body: string, authorization?: string): Request {
  const headers = new Headers({
    "content-type": "application/json",
  });
  if (authorization) {
    headers.set("Authorization", authorization);
  }

  return new Request("https://example.com/run-bundles", {
    method: "POST",
    headers,
    body,
  });
}

test("run bundle upload stores one artifact object and projection rows", async () => {
  const env = buildEnv();
  const payloadHash = sha256Base64(new Uint8Array([1, 2, 3, 4]));
  const body = JSON.stringify({
    schema_version: 3,
    player_account_id: "player-account-001",
    submitted_at_utc: new Date().toISOString(),
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [1, 2, 3, 4],
    run_projection: {
      run_id: "run-001",
      status: "completed",
      hero_id: "hero-a",
      hero_name: "HeroA",
      player_rank: "Gold",
      player_rating: 1700,
      player_position: null,
      started_at_utc: "2026-04-10T00:00:00.000Z",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
      final_day: 10,
      final_wins: 10,
      final_losses: 2,
      final_player_rank: "Gold",
      final_player_rating: 1720,
      final_player_position: null,
    },
    battle_projections: [
      {
        battle_id: "battle-001",
        run_id: "run-001",
        recorded_at_utc: "2026-04-10T00:30:00.000Z",
        day: 8,
        player_name: "RemoteA",
        player_account_id: "remote-a",
        player_hero: "HeroA",
        player_rank: "Gold",
        player_rating: 1700,
        player_level: 10,
        opponent_name: "Local",
        opponent_account_id: "player-account-001",
        opponent_hero: "HeroB",
        opponent_rank: "Gold",
        opponent_rating: 1710,
        opponent_level: 11,
        result: "Won",
        replay_available: true,
      },
      {
        battle_id: "battle-002",
        run_id: "run-001",
        recorded_at_utc: "2026-04-10T00:40:00.000Z",
        day: 10,
        player_name: "RemoteB",
        player_account_id: "remote-b",
        player_hero: "HeroA",
        player_rank: "Platinum",
        player_rating: 1800,
        player_level: 12,
        opponent_name: "Local",
        opponent_account_id: "player-account-001",
        opponent_hero: "HeroB",
        opponent_rank: "Platinum",
        opponent_rating: 1790,
        opponent_level: 12,
        result: "Lost",
        replay_available: true,
      },
    ],
  });

  const response = await worker.fetch(buildUploadRequest(body), env as never);

  assert.equal(response.status, 200);
  assert.equal(env.RUN_BUNDLE_BUCKET.objects.size, 1);
  assert.equal(env.DB.v3RunBundles.size, 1);
  assert.equal(env.DB.v3Runs.size, 1);
  assert.equal(env.DB.v3Battles.size, 2);
  const bundle = Array.from(env.DB.v3RunBundles.values())[0];
  const run = env.DB.v3Runs.get("run-001");
  const battle = env.DB.v3Battles.get("battle-001");
  assert.equal(bundle?.installation_id, null);
  assert.equal(run?.installation_id, null);
  assert.equal(battle?.installation_id, null);
  assert.equal(
    bundle?.object_key,
    `run-bundles/player-account-001/run-001/${toBase64UrlSegment(payloadHash)}.mpack.gz`,
  );
  assert.deepEqual(env.KNOWN_PLAYER_ACCOUNTS.entries.get("player-account-001"), {
    value: "1",
    expirationTtl: 7 * 24 * 60 * 60,
  });
});

test("run bundle upload accepts requests without Authorization header", async () => {
  const env = buildEnv();

  const body = JSON.stringify({
    schema_version: 3,
    player_account_id: "player-account-unsigned",
    submitted_at_utc: new Date().toISOString(),
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [1, 2, 3],
    run_projection: {
      run_id: "run-unsigned",
      status: "completed",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
    },
    battle_projections: [
      {
        battle_id: "battle-unsigned",
        run_id: "run-unsigned",
        recorded_at_utc: "2026-04-10T00:30:00.000Z",
        day: 10,
        player_rating: 1700,
        opponent_account_id: "player-account-unsigned",
        replay_available: true,
      },
    ],
  });

  const response = await worker.fetch(buildUploadRequest(body), env as never);

  assert.equal(response.status, 200);
  assert.equal(env.RUN_BUNDLE_BUCKET.objects.size, 1);
  assert.equal(env.DB.v3RunBundles.size, 1);
  assert.equal(env.DB.v3Runs.size, 1);
  assert.equal(env.DB.v3Battles.size, 1);
});

test("run bundle upload ignores bogus Authorization header", async () => {
  const env = buildEnv();

  const body = JSON.stringify({
    schema_version: 3,
    player_account_id: "player-account-garbage-auth",
    submitted_at_utc: new Date().toISOString(),
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [4, 5, 6],
    run_projection: {
      run_id: "run-garbage-auth",
      status: "completed",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
    },
    battle_projections: [
      {
        battle_id: "battle-garbage-auth",
        run_id: "run-garbage-auth",
        recorded_at_utc: "2026-04-10T00:30:00.000Z",
        day: 10,
        player_rating: 1700,
        opponent_account_id: "player-account-garbage-auth",
        replay_available: true,
      },
    ],
  });

  const response = await worker.fetch(
    buildUploadRequest(body, "Bearer garbage"),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.equal(env.RUN_BUNDLE_BUCKET.objects.size, 1);
  assert.equal(env.DB.v3RunBundles.size, 1);
  assert.equal(env.DB.v3Runs.size, 1);
  assert.equal(env.DB.v3Battles.size, 1);
});

test("run bundle upload accepts requests without player account id", async () => {
  const env = buildEnv();
  const payloadHash = sha256Base64(new Uint8Array([7, 8, 9]));

  const body = JSON.stringify({
    schema_version: 3,
    submitted_at_utc: new Date().toISOString(),
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [7, 8, 9],
    run_projection: {
      run_id: "run-no-player-account",
      status: "completed",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
    },
    battle_projections: [
      {
        battle_id: "battle-no-player-account",
        run_id: "run-no-player-account",
        recorded_at_utc: "2026-04-10T00:30:00.000Z",
        day: 10,
        player_rating: 1700,
        replay_available: true,
      },
    ],
  });

  const response = await worker.fetch(buildUploadRequest(body), env as never);

  assert.equal(response.status, 200);
  const bundle = Array.from(env.DB.v3RunBundles.values())[0];
  const run = env.DB.v3Runs.get("run-no-player-account");
  assert.equal(bundle?.player_account_id, "anonymous-player");
  assert.equal(bundle?.installation_id, null);
  assert.equal(run?.installation_id, null);
  assert.equal(env.DB.v3Battles.size, 0);
  assert.equal(
    bundle?.object_key,
    `run-bundles/anonymous-player/run-no-player-account/${toBase64UrlSegment(payloadHash)}.mpack.gz`,
  );
});

test("run bundle upload rejects mismatched battle run ids", async () => {
  const env = buildEnv();

  const body = JSON.stringify({
    schema_version: 3,
    player_account_id: "player-account-001",
    submitted_at_utc: new Date().toISOString(),
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [1],
    run_projection: {
      run_id: "run-match",
      status: "completed",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
    },
    battle_projections: [
      { battle_id: "battle-001", run_id: "run-other", recorded_at_utc: "2026-04-10T00:00:00.000Z" },
    ],
  });

  const response = await worker.fetch(buildUploadRequest(body), env as never);

  assert.equal(response.status, 400);
});

test("run bundle upload applies configured artifact retention", async () => {
  const env = buildEnv();
  env.RUN_BUNDLE_RETENTION_DAYS = "9";

  const body = JSON.stringify({
    schema_version: 3,
    player_account_id: "player-account-001",
    submitted_at_utc: new Date().toISOString(),
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [1, 2, 3],
    run_projection: {
      run_id: "run-retention",
      status: "completed",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
    },
    battle_projections: [],
  });

  const response = await worker.fetch(buildUploadRequest(body), env as never);

  assert.equal(response.status, 200);
  assert.equal(env.RUN_BUNDLE_BUCKET.lastPutOptions?.customMetadata?.retention_days, "9");
});

test("run bundle upload only projects battles whose opponent account id is known", async () => {
  const env = buildEnv();
  await env.KNOWN_PLAYER_ACCOUNTS.put("known-opponent", "1", {
    expirationTtl: 7 * 24 * 60 * 60,
  });

  const body = JSON.stringify({
    schema_version: 3,
    player_account_id: "player-account-001",
    submitted_at_utc: new Date().toISOString(),
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [1, 2],
    run_projection: {
      run_id: "run-gated",
      status: "completed",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
    },
    battle_projections: [
      {
        battle_id: "battle-keep",
        run_id: "run-gated",
        recorded_at_utc: "2026-04-10T00:30:00.000Z",
        day: 6,
        player_rating: 1700,
        opponent_account_id: "known-opponent",
        replay_available: true,
      },
      {
        battle_id: "battle-drop",
        run_id: "run-gated",
        recorded_at_utc: "2026-04-10T00:40:00.000Z",
        day: 5,
        player_rating: 1200,
        opponent_account_id: "unknown-opponent",
        replay_available: true,
      },
    ],
  });

  const response = await worker.fetch(buildUploadRequest(body), env as never);

  assert.equal(response.status, 200);
  assert.equal(env.DB.v3Battles.size, 1);
  assert.ok(env.DB.v3Battles.has("battle-keep"));
});

test("run bundle upload trusts the current uploader account id immediately", async () => {
  const env = buildEnv();

  const body = JSON.stringify({
    schema_version: 3,
    player_account_id: "player-account-001",
    submitted_at_utc: new Date().toISOString(),
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [4, 5, 6],
    run_projection: {
      run_id: "run-uploader-trust",
      status: "completed",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
    },
    battle_projections: [
      {
        battle_id: "battle-uploader-trust",
        run_id: "run-uploader-trust",
        recorded_at_utc: "2026-04-10T00:30:00.000Z",
        opponent_account_id: "player-account-001",
        replay_available: true,
      },
    ],
  });

  const response = await worker.fetch(buildUploadRequest(body), env as never);

  assert.equal(response.status, 200);
  assert.equal(env.DB.v3Battles.size, 1);
  assert.ok(env.DB.v3Battles.has("battle-uploader-trust"));
  assert.deepEqual(env.KNOWN_PLAYER_ACCOUNTS.entries.get("player-account-001"), {
    value: "1",
    expirationTtl: 7 * 24 * 60 * 60,
  });
});

test("run bundle upload accepts duplicate payload retries idempotently", async () => {
  const env = buildEnv();
  const payloadHash = sha256Base64(new Uint8Array([1, 2, 3, 4]));
  const body = JSON.stringify({
    schema_version: 3,
    player_account_id: "player-account-001",
    submitted_at_utc: "2026-04-10T01:00:00.000Z",
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [1, 2, 3, 4],
    run_projection: {
      run_id: "run-dup",
      status: "completed",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
    },
    battle_projections: [
      {
        battle_id: "battle-dup",
        run_id: "run-dup",
        recorded_at_utc: "2026-04-10T00:30:00.000Z",
        day: 10,
        player_rating: 1700,
        opponent_account_id: "player-account-001",
        replay_available: true,
      },
    ],
  });

  const firstResponse = await worker.fetch(buildUploadRequest(body), env as never);
  assert.equal(firstResponse.status, 200);
  const firstJson = (await firstResponse.json()) as {
    bundle_id: string;
    object_key: string;
  };

  const secondResponse = await worker.fetch(buildUploadRequest(body), env as never);

  assert.equal(secondResponse.status, 200);
  const secondJson = (await secondResponse.json()) as {
    bundle_id: string;
    object_key: string;
  };
  assert.equal(secondJson.bundle_id, firstJson.bundle_id);
  assert.equal(
    secondJson.object_key,
    `run-bundles/player-account-001/run-dup/${toBase64UrlSegment(payloadHash)}.mpack.gz`,
  );
  assert.equal(secondJson.object_key, firstJson.object_key);
  assert.equal(env.RUN_BUNDLE_BUCKET.objects.size, 1);
  assert.equal(env.DB.v3RunBundles.size, 1);
  assert.equal(env.DB.v3Runs.size, 1);
  assert.equal(env.DB.v3Battles.size, 1);
  const bundle = Array.from(env.DB.v3RunBundles.values())[0];
  const run = env.DB.v3Runs.get("run-dup");
  const battle = env.DB.v3Battles.get("battle-dup");
  assert.equal(bundle?.installation_id, null);
  assert.equal(run?.installation_id, null);
  assert.equal(battle?.installation_id, null);
});
