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

test("persists verified replay uploads", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();

  const registerResponse = await worker.fetch(
    new Request("https://example.com/clients/register", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        install_id: "install-001",
        plugin_version: "1.9.0",
        purpose: "replays",
        public_key: {
          modulus_b64: modulusB64,
          exponent_b64: exponentB64,
        },
      }),
    }),
    env as never,
  );
  assert.equal(registerResponse.status, 200);
  const registerJson = (await registerResponse.json()) as { client_id: string };
  env.DB.pvpBattles.set("battle-001", {
    battle_id: "battle-001",
    run_id: "run-001",
    source_client_id: "runs-client-001",
    recorded_at_utc: "2026-03-29T12:00:00.000Z",
    day: 8,
    hour: 1,
    encounter_id: "encounter-001",
    player_name: "Uploader",
    player_account_id: "uploader-account",
    player_hero: "Dooley",
    player_rank: "Legendary",
    player_rating: 1800,
    player_level: 11,
    opponent_name: "Me",
    opponent_account_id: "my-account",
    opponent_hero: "Vanessa",
    opponent_rank: "Legendary",
    opponent_rating: 1750,
    opponent_level: 10,
    combat_kind: "PVPCombat",
    result: "win",
    winner_combatant_id: "Player",
    loser_combatant_id: "Opponent",
    summary_json: JSON.stringify({ battle_id: "battle-001" }),
    replay_available: 0,
    projection_version: 1,
    created_at_utc: "2026-03-29T12:00:00.000Z",
    updated_at_utc: "2026-03-29T12:00:00.000Z",
  });

  const payload = JSON.stringify({
    battle_id: "battle-001",
    client_id: registerJson.client_id,
    replay_payload: {
      battle_id: "battle-001",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-001";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/replays/upload",
      registerJson.client_id,
      "install-001",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": registerJson.client_id,
        "x-bpp-install-id": "install-001",
        "x-bpp-battle-id": "battle-001",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-nonce": nonce,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as { object_key: string };
  assert.equal(json.object_key, `replays/${registerJson.client_id}/battle-001/${bodyHash}.json`);
  const replayUpload = env.DB.replayUploads.get("battle-001");
  assert.equal(replayUpload?.client_id, registerJson.client_id);
  assert.equal(replayUpload?.payload_bytes, new TextEncoder().encode(payload).byteLength);
  assert.equal(replayUpload?.schema_version, 1);
  assert.equal(replayUpload?.content_type, "application/json");
  assert.ok((replayUpload?.created_at_utc ?? "").length > 0);
  assert.ok((replayUpload?.updated_at_utc ?? "").length > 0);
  assert.equal(env.DB.pvpBattles.get("battle-001")?.replay_available, 1);
  assert.ok(env.REPLAY_BUCKET.objects.has(json.object_key));
});

test("re-uploading the same replay payload remains idempotent", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "replays-client-repeat";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-repeat-upload",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    battle_id: "battle-repeat",
    replay_payload: {
      battle_id: "battle-repeat",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });
  const bodyHash = sha256Base64(payload);

  for (const nonce of ["nonce-repeat-1", "nonce-repeat-2"]) {
    const timestamp = new Date().toISOString();
    const signature = signCanonical(
      privateKey,
      canonicalRequest(
        "POST",
        "/replays/upload",
        clientId,
        "install-repeat-upload",
        timestamp,
        nonce,
        bodyHash,
      ),
    );

    const response = await worker.fetch(
      new Request("https://example.com/replays/upload", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "x-bpp-client-id": clientId,
          "x-bpp-install-id": "install-repeat-upload",
          "x-bpp-battle-id": "battle-repeat",
          "x-bpp-plugin-version": "1.9.0",
          "x-bpp-timestamp": timestamp,
          "x-bpp-nonce": nonce,
          "x-bpp-content-sha256": bodyHash,
          "x-bpp-signature-alg": "rsa-pkcs1-sha256",
          "x-bpp-signature": signature,
        },
        body: payload,
      }),
      env as never,
    );

    assert.equal(response.status, 200);
  }

  assert.equal(env.DB.replayUploads.size, 1);
  assert.equal(env.REPLAY_BUCKET.objects.size, 1);
});

test("rejects replay uploads with incomplete replay payloads", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "replays-client-invalid";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-invalid-replay",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    battle_id: "battle-invalid",
    replay_payload: {
      battle_id: "battle-invalid",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
    },
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-invalid-replay";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/replays/upload",
      clientId,
      "install-invalid-replay",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-invalid-replay",
        "x-bpp-battle-id": "battle-invalid",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-nonce": nonce,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "invalid_replay_payload" });
  assert.equal(env.DB.replayUploads.size, 0);
  assert.equal(env.REPLAY_BUCKET.objects.size, 0);
});
