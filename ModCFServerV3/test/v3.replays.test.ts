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

test("replay-link requires battle ownership", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_replay", {
    installation_id: "inst_replay",
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
  env.DB.v3Battles.set("battle-foreign", {
    battle_id: "battle-foreign",
    run_id: "run-foreign",
    installation_id: "inst_replay",
    player_account_id: "remote-player",
    bundle_id: "bundle-foreign",
    recorded_at_utc: new Date().toISOString(),
    day: 7,
    player_name: "Remote",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "OtherLocal",
    opponent_account_id: "other-player",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });

  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64("");
  const signature = signCanonical(
    privateKey,
    canonicalRequestV3({
      method: "POST",
      path: "/ghost-battles/battle-foreign/replay-link",
      query: "",
      installationId: "inst_replay",
      timestamp,
      bodyHash,
    }),
  );

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-foreign/replay-link", {
      method: "POST",
      headers: {
        "x-bpp-installation-id": "inst_replay",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature": signature,
      },
    }),
    env as never,
  );

  assert.equal(response.status, 403);
  assert.deepEqual(await response.json(), { error: "battle_forbidden" });
});

test("download replay accepts valid short-lived token", async () => {
  const env = buildEnv();
  env.DB.v3ReplayTokens.set("token-valid", {
    token: "token-valid",
    battle_id: "battle-owned",
    requested_by_player_account_id: "player-account-001",
    expires_at_utc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    created_at_utc: new Date().toISOString(),
    used_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.v3Battles.set("battle-owned", {
    battle_id: "battle-owned",
    run_id: "run-owned",
    installation_id: "inst_replay",
    player_account_id: "remote-player",
    bundle_id: "bundle-owned",
    recorded_at_utc: new Date().toISOString(),
    day: 9,
    player_name: "Remote",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1600,
    player_level: 10,
    opponent_name: "Local",
    opponent_account_id: "player-account-001",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1610,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });
  env.DB.v3RunBundles.set("bundle-owned", {
    bundle_id: "bundle-owned",
    installation_id: "inst_replay",
    player_account_id: "remote-player",
    run_id: "run-owned",
    payload_hash: "payload-hash",
    schema_version: 3,
    object_key: "run-bundles/remote-player/inst_replay/run-owned/payload-hash.mpack.gz",
    codec: "application/json",
    size_bytes: 12,
    submitted_at_utc: new Date().toISOString(),
    created_at_utc: new Date().toISOString(),
  });
  await env.PVP_BATTLE_BUCKET.put(
    "run-bundles/remote-player/inst_replay/run-owned/payload-hash.mpack.gz",
    new TextEncoder().encode('{"battle_id":"battle-owned"}'),
    {
      httpMetadata: { contentType: "application/json" },
    },
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-valid", {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.equal(await response.text(), '{"battle_id":"battle-owned"}');
});

test("download replay returns artifact_expired when artifact is no longer available", async () => {
  const env = buildEnv();
  env.DB.v3ReplayTokens.set("token-expired-artifact", {
    token: "token-expired-artifact",
    battle_id: "battle-expired",
    requested_by_player_account_id: "player-account-001",
    expires_at_utc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    created_at_utc: new Date().toISOString(),
    used_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.v3Battles.set("battle-expired", {
    battle_id: "battle-expired",
    run_id: "run-expired",
    installation_id: "inst_replay",
    player_account_id: "remote-player",
    bundle_id: "bundle-expired",
    recorded_at_utc: new Date().toISOString(),
    day: 9,
    player_name: "Remote",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1600,
    player_level: 10,
    opponent_name: "Local",
    opponent_account_id: "player-account-001",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1610,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });
  env.DB.v3RunBundles.set("bundle-expired", {
    bundle_id: "bundle-expired",
    installation_id: "inst_replay",
    player_account_id: "remote-player",
    run_id: "run-expired",
    payload_hash: "payload-hash-expired",
    schema_version: 3,
    object_key: "run-bundles/remote-player/inst_replay/run-expired/payload-hash-expired.mpack.gz",
    codec: "application/json",
    size_bytes: 12,
    submitted_at_utc: new Date().toISOString(),
    created_at_utc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-expired-artifact", {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 410);
  assert.deepEqual(await response.json(), { error: "artifact_expired" });
});
