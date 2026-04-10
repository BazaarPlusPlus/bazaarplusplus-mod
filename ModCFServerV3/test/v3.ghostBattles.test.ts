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

test("ghost-battles ignores caller days and uses the server lookback window", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_ghost", {
    installation_id: "inst_ghost",
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

  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64("");
  const query = "days=99&limit=200";
  const signature = signCanonical(
    privateKey,
    canonicalRequestV3({
      method: "GET",
      path: "/ghost-battles",
      query,
      installationId: "inst_ghost",
      timestamp,
      bodyHash,
    }),
  );

  const response = await worker.fetch(
    new Request(`https://example.com/ghost-battles?${query}`, {
      method: "GET",
      headers: {
        "x-bpp-installation-id": "inst_ghost",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature": signature,
      },
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
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  env.DB.v3Installations.set("inst_ghost", {
    installation_id: "inst_ghost",
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

  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64("");
  const query = "limit=1";
  const signature = signCanonical(
    privateKey,
    canonicalRequestV3({
      method: "GET",
      path: "/ghost-battles",
      query,
      installationId: "inst_ghost",
      timestamp,
      bodyHash,
    }),
  );

  const response = await worker.fetch(
    new Request(`https://example.com/ghost-battles?${query}`, {
      method: "GET",
      headers: {
        "x-bpp-installation-id": "inst_ghost",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature": signature,
      },
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string }>;
  };
  assert.deepEqual(json.battles.map((battle) => battle.battle_id), ["battle-003"]);
});
