import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import {
  canonicalRequest,
  generateClientKeyPair,
  sha256Base64,
  signCanonical,
} from "./helpers/crypto";
import { buildEnv } from "./helpers/mockEnv";

test("stores signed battle artifacts in D1 and replay payloads in R2", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-battle-001";
  const installId = "install-battle-001";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: installId,
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.playerLinks.set(clientId, {
    client_id: clientId,
    player_account_id: "uploader-account",
    bound_at_utc: new Date().toISOString(),
    last_confirmed_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    battle_id: "battle-001",
    run_id: "run-001",
    schema_version: 1,
    battle_manifest: {
      battle_id: "battle-001",
      run_id: "run-001",
      recorded_at_utc: "2026-04-01T00:00:00.000Z",
      day: 10,
      hour: 2,
      combat_kind: "PVPCombat",
      participants: {
        player_name: "Uploader",
        player_account_id: "uploader-account",
        player_hero: "Dooley",
        player_rank: "Legendary",
        player_rating: 1800,
        player_level: 12,
        opponent_name: "Target",
        opponent_account_id: "target-account",
        opponent_hero: "Vanessa",
        opponent_rank: "Legendary",
        opponent_rating: 1780,
        opponent_level: 12,
      },
      outcome: {
        result: "win",
        winner_combatant_id: "Player",
        loser_combatant_id: "Opponent",
      },
      snapshots: {
        player_board: {},
        opponent_board: {},
      },
    },
    replay_payload: {
      battle_id: "battle-001",
      version: 2,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });
  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64(payload);
  const signature = signCanonical(
    privateKey,
    canonicalRequest("POST", "/battles", clientId, installId, timestamp, bodyHash),
  );

  const response = await worker.fetch(
    new Request("https://example.com/battles", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": installId,
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as { status: string; object_key: string };
  assert.equal(json.status, "accepted");
  assert.ok(json.object_key);

  const battle = env.DB.battles.get("battle-001");
  assert.equal(battle?.client_id, clientId);
  assert.equal(battle?.uploader_player_account_id, "uploader-account");
  assert.equal(battle?.replay_schema_version, 2);
  assert.equal(battle?.opponent_account_id, "target-account");
  assert.equal(battle?.replay_object_key, json.object_key);
  assert.ok(env.PVP_BATTLE_BUCKET.objects.has(json.object_key));
});
