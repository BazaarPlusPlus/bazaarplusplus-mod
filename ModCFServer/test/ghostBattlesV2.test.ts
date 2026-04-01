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

function buildSignedRequest(
  url: string,
  clientId: string,
  installId: string,
  privateKey: Parameters<typeof signCanonical>[0],
  body?: string,
): Request {
  const timestamp = new Date().toISOString();
  const payload = body ?? "";
  const bodyHash = sha256Base64(payload);
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      body ? "POST" : "GET",
      new URL(url).pathname,
      clientId,
      installId,
      timestamp,
      bodyHash,
    ),
  );

  return new Request(url, {
    method: body ? "POST" : "GET",
    headers: {
      "content-type": "application/json",
      "x-bpp-client-id": clientId,
      "x-bpp-install-id": installId,
      "x-bpp-timestamp": timestamp,
      "x-bpp-content-sha256": bodyHash,
      "x-bpp-signature-alg": "rsa-pkcs1-sha256",
      "x-bpp-signature": signature,
    },
    body,
  });
}

test("queries ghost battles against the currently bound player account", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-ghost-001";
  const installId = "install-ghost-001";
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
    player_account_id: "me-account",
    bound_at_utc: new Date().toISOString(),
    last_confirmed_at_utc: new Date().toISOString(),
  });
  env.DB.battles.set("battle-001", {
    battle_id: "battle-001",
    run_id: "run-001",
    client_id: "uploader-client",
    uploader_player_account_id: "uploader-account",
    recorded_at_utc: "2026-04-01T03:00:00.000Z",
    day: 10,
    hour: 3,
    player_name: "Uploader",
    player_account_id: "uploader-account",
    player_hero: "Dooley",
    player_rank: "Legendary",
    player_rating: 1820,
    player_level: 12,
    opponent_name: "Me",
    opponent_account_id: "me-account",
    opponent_hero: "Vanessa",
    opponent_rank: "Legendary",
    opponent_rating: 1810,
    opponent_level: 12,
    combat_kind: "PVPCombat",
    result: "win",
    winner_combatant_id: "Player",
    loser_combatant_id: "Opponent",
    replay_schema_version: 2,
    replay_object_key: "battle-replays/uploader/battle-001/hash.json",
    replay_size_bytes: 128,
    created_at_utc: "2026-04-01T03:00:00.000Z",
    updated_at_utc: "2026-04-01T03:00:00.000Z",
  });

  const response = await worker.fetch(
    buildSignedRequest(
      "https://example.com/players/me/ghost-battles",
      clientId,
      installId,
      privateKey,
    ),
    env as never,
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as { battles: Array<{ battle_id: string }> };
  assert.equal(json.battles.length, 1);
  assert.equal(json.battles[0]?.battle_id, "battle-001");
});

test("creates a replay token and downloads the stored replay payload", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-replay-001";
  const installId = "install-replay-001";
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
    player_account_id: "me-account",
    bound_at_utc: new Date().toISOString(),
    last_confirmed_at_utc: new Date().toISOString(),
  });
  env.DB.battles.set("battle-002", {
    battle_id: "battle-002",
    run_id: "run-002",
    client_id: "uploader-client",
    uploader_player_account_id: "uploader-account",
    recorded_at_utc: "2026-04-01T04:00:00.000Z",
    day: 10,
    hour: 4,
    player_name: "Uploader",
    player_account_id: "uploader-account",
    player_hero: "Dooley",
    player_rank: "Legendary",
    player_rating: 1820,
    player_level: 12,
    opponent_name: "Me",
    opponent_account_id: "me-account",
    opponent_hero: "Vanessa",
    opponent_rank: "Legendary",
    opponent_rating: 1810,
    opponent_level: 12,
    combat_kind: "PVPCombat",
    result: "loss",
    winner_combatant_id: "Opponent",
    loser_combatant_id: "Player",
    replay_schema_version: 2,
    replay_object_key: "battle-replays/uploader/battle-002/hash.json",
    replay_size_bytes: 128,
    created_at_utc: "2026-04-01T04:00:00.000Z",
    updated_at_utc: "2026-04-01T04:00:00.000Z",
  });
  await env.PVP_BATTLE_BUCKET.put(
    "battle-replays/uploader/battle-002/hash.json",
    new TextEncoder().encode(JSON.stringify({ replay_payload: { battle_id: "battle-002" } })),
  );

  const linkResponse = await worker.fetch(
    buildSignedRequest(
      "https://example.com/players/me/ghost-battles/battle-002/replay-link",
      clientId,
      installId,
      privateKey,
      JSON.stringify({}),
    ),
    env as never,
  );
  assert.equal(linkResponse.status, 200);
  const linkJson = (await linkResponse.json()) as { download_url: string };
  assert.match(linkJson.download_url, /\/replays\//);

  const downloadResponse = await worker.fetch(
    new Request(linkJson.download_url, { method: "GET" }),
    env as never,
  );
  assert.equal(downloadResponse.status, 200);
  const downloadJson = (await downloadResponse.json()) as {
    replay_payload: { battle_id: string };
  };
  assert.equal(downloadJson.replay_payload.battle_id, "battle-002");
});
