import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import {
  canonicalRequestV3,
  generateClientKeyPair,
  sha256Base64,
  signCanonical,
} from "./helpers/crypto";
import { buildEnv } from "./helpers/mockEnv";

function buildSignedRequest(input: {
  body: string;
  installationId: string;
  privateKey: CryptoKey | unknown;
}) {
  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64(input.body);
  const signature = signCanonical(
    input.privateKey as never,
    canonicalRequestV3({
      method: "POST",
      path: "/run-bundles",
      query: "",
      installationId: input.installationId,
      timestamp,
      bodyHash,
    }),
  );

  return {
    timestamp,
    bodyHash,
    signature,
  };
}

test("run bundle upload stores one artifact object and projection rows", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_bundle", {
    installation_id: "inst_bundle",
    player_account_id: "player-account-001",
    public_key: JSON.stringify({
      modulus_b64: modulusB64,
      exponent_b64: exponentB64,
    }),
    status: "active",
    created_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });

  const body = JSON.stringify({
    schema_version: 3,
    installation_id: "inst_bundle",
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
  const signed = buildSignedRequest({
    body,
    installationId: "inst_bundle",
    privateKey,
  });

  const response = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-installation-id": "inst_bundle",
        "x-bpp-timestamp": signed.timestamp,
        "x-bpp-content-sha256": signed.bodyHash,
        "x-bpp-signature": signed.signature,
      },
      body,
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.equal(env.RUN_BUNDLE_BUCKET.objects.size, 1);
  assert.equal(env.DB.v3RunBundles.size, 1);
  assert.equal(env.DB.v3Runs.size, 1);
  assert.equal(env.DB.v3Battles.size, 2);
});

test("run bundle upload accepts unsigned uploads without installation auth", async () => {
  const env = buildEnv();

  const body = JSON.stringify({
    schema_version: 3,
    installation_id: "inst_unsigned",
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

  const response = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "POST",
      headers: {
        "content-type": "application/json",
      },
      body,
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.equal(env.RUN_BUNDLE_BUCKET.objects.size, 1);
  assert.equal(env.DB.v3RunBundles.size, 1);
  assert.equal(env.DB.v3Runs.size, 1);
  assert.equal(env.DB.v3Battles.size, 1);
});

test("run bundle upload rejects duplicated battle ids in battle_projections", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_bundle", {
    installation_id: "inst_bundle",
    player_account_id: "player-account-001",
    public_key: JSON.stringify({
      modulus_b64: modulusB64,
      exponent_b64: exponentB64,
    }),
    status: "active",
    created_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });

  const body = JSON.stringify({
    schema_version: 3,
    installation_id: "inst_bundle",
    player_account_id: "player-account-001",
    submitted_at_utc: new Date().toISOString(),
    artifact_codec: "application/x-bpp-runbundle+msgpack+gzip",
    artifact_bytes: [1],
    run_projection: {
      run_id: "run-dup",
      status: "completed",
      ended_at_utc: "2026-04-10T01:00:00.000Z",
    },
    battle_projections: [
      { battle_id: "battle-dup", run_id: "run-dup", recorded_at_utc: "2026-04-10T00:00:00.000Z" },
      { battle_id: "battle-dup", run_id: "run-dup", recorded_at_utc: "2026-04-10T00:01:00.000Z" },
    ],
  });
  const signed = buildSignedRequest({
    body,
    installationId: "inst_bundle",
    privateKey,
  });

  const response = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-installation-id": "inst_bundle",
        "x-bpp-timestamp": signed.timestamp,
        "x-bpp-content-sha256": signed.bodyHash,
        "x-bpp-signature": signed.signature,
      },
      body,
    }),
    env as never,
  );

  assert.equal(response.status, 400);
});

test("run bundle upload rejects mismatched battle run ids", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_bundle", {
    installation_id: "inst_bundle",
    player_account_id: "player-account-001",
    public_key: JSON.stringify({
      modulus_b64: modulusB64,
      exponent_b64: exponentB64,
    }),
    status: "active",
    created_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });

  const body = JSON.stringify({
    schema_version: 3,
    installation_id: "inst_bundle",
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
  const signed = buildSignedRequest({
    body,
    installationId: "inst_bundle",
    privateKey,
  });

  const response = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-installation-id": "inst_bundle",
        "x-bpp-timestamp": signed.timestamp,
        "x-bpp-content-sha256": signed.bodyHash,
        "x-bpp-signature": signed.signature,
      },
      body,
    }),
    env as never,
  );

  assert.equal(response.status, 400);
});

test("run bundle upload applies configured artifact retention", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_bundle", {
    installation_id: "inst_bundle",
    player_account_id: "player-account-001",
    public_key: JSON.stringify({
      modulus_b64: modulusB64,
      exponent_b64: exponentB64,
    }),
    status: "active",
    created_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });

  const body = JSON.stringify({
    schema_version: 3,
    installation_id: "inst_bundle",
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
  const signed = buildSignedRequest({
    body,
    installationId: "inst_bundle",
    privateKey,
  });

  const response = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-installation-id": "inst_bundle",
        "x-bpp-timestamp": signed.timestamp,
        "x-bpp-content-sha256": signed.bodyHash,
        "x-bpp-signature": signed.signature,
      },
      body,
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.equal(env.RUN_BUNDLE_BUCKET.lastPutOptions?.customMetadata?.retention_days, "5");
});

test("run bundle upload only projects battles that pass ingest gate", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_bundle", {
    installation_id: "inst_bundle",
    player_account_id: "player-account-001",
    public_key: JSON.stringify({
      modulus_b64: modulusB64,
      exponent_b64: exponentB64,
    }),
    status: "active",
    created_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });

  const body = JSON.stringify({
    schema_version: 3,
    installation_id: "inst_bundle",
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
        day: 5,
        player_rating: 1700,
        opponent_account_id: "player-account-001",
        replay_available: true,
      },
      {
        battle_id: "battle-drop",
        run_id: "run-gated",
        recorded_at_utc: "2026-04-10T00:40:00.000Z",
        day: 5,
        player_rating: 1200,
        opponent_account_id: "player-account-001",
        replay_available: true,
      },
    ],
  });
  const signed = buildSignedRequest({
    body,
    installationId: "inst_bundle",
    privateKey,
  });

  const response = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-installation-id": "inst_bundle",
        "x-bpp-timestamp": signed.timestamp,
        "x-bpp-content-sha256": signed.bodyHash,
        "x-bpp-signature": signed.signature,
      },
      body,
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.equal(env.DB.v3Battles.size, 1);
  assert.ok(env.DB.v3Battles.has("battle-keep"));
});

test("run bundle upload accepts duplicate payload retries idempotently", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_bundle", {
    installation_id: "inst_bundle",
    player_account_id: "player-account-001",
    public_key: JSON.stringify({
      modulus_b64: modulusB64,
      exponent_b64: exponentB64,
    }),
    status: "active",
    created_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });

  const body = JSON.stringify({
    schema_version: 3,
    installation_id: "inst_bundle",
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
  const signed = buildSignedRequest({
    body,
    installationId: "inst_bundle",
    privateKey,
  });

  const firstResponse = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-installation-id": "inst_bundle",
        "x-bpp-timestamp": signed.timestamp,
        "x-bpp-content-sha256": signed.bodyHash,
        "x-bpp-signature": signed.signature,
      },
      body,
    }),
    env as never,
  );
  assert.equal(firstResponse.status, 200);
  const firstJson = (await firstResponse.json()) as {
    bundle_id: string;
    object_key: string;
  };

  const secondResponse = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-installation-id": "inst_bundle",
        "x-bpp-timestamp": signed.timestamp,
        "x-bpp-content-sha256": signed.bodyHash,
        "x-bpp-signature": signed.signature,
      },
      body,
    }),
    env as never,
  );

  assert.equal(secondResponse.status, 200);
  const secondJson = (await secondResponse.json()) as {
    bundle_id: string;
    object_key: string;
  };
  assert.equal(secondJson.bundle_id, firstJson.bundle_id);
  assert.equal(secondJson.object_key, firstJson.object_key);
  assert.equal(env.RUN_BUNDLE_BUCKET.objects.size, 1);
  assert.equal(env.DB.v3RunBundles.size, 1);
  assert.equal(env.DB.v3Runs.size, 1);
  assert.equal(env.DB.v3Battles.size, 1);
});
