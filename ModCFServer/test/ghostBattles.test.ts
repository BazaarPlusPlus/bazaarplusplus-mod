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

const EMPTY_BODY_HASH = sha256Base64("");

function buildSignedRequest(
  method: "GET" | "POST",
  url: string,
  clientId: string,
  installId: string,
  privateKey: Parameters<typeof signCanonical>[0],
  nonce: string,
): Request {
  const timestamp = new Date().toISOString();
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      method,
      new URL(url).pathname,
      clientId,
      installId,
      timestamp,
      nonce,
      EMPTY_BODY_HASH,
    ),
  );

  return new Request(url, {
    method,
    headers: {
      "x-bpp-client-id": clientId,
      "x-bpp-install-id": installId,
      "x-bpp-timestamp": timestamp,
      "x-bpp-nonce": nonce,
      "x-bpp-content-sha256": EMPTY_BODY_HASH,
      "x-bpp-signature-alg": "rsa-pkcs1-sha256",
      "x-bpp-signature": signature,
    },
  });
}

test("lists recent ghost battles against the bound player account", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-ghost-query";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-ghost-query",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });
  env.DB.clientUidBindings.set("binding-ghost-query", {
    binding_id: "binding-ghost-query",
    client_id: clientId,
    uid: "uid-ghost-query",
    bound_at_utc: new Date().toISOString(),
    unbound_at_utc: null,
  });
  env.DB.uidPlayerAccounts.set("uid-ghost-query:my-account", {
    uid: "uid-ghost-query",
    player_account_id: "my-account",
    first_seen_at_utc: "2026-03-28T00:00:00.000Z",
    last_seen_at_utc: "2026-03-29T12:00:00.000Z",
    last_client_id: clientId,
  });
  env.DB.pvpBattles.set("battle-ghost-001", {
    battle_id: "battle-ghost-001",
    run_id: "run-ghost-001",
    source_client_id: "remote-client",
    recorded_at_utc: "2099-03-29T12:00:00.000Z",
    day: 9,
    hour: 2,
    encounter_id: "encounter-ghost-001",
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
    result: "loss",
    winner_combatant_id: "Opponent",
    loser_combatant_id: "Player",
    payload_json: JSON.stringify({
      battle_id: "battle-ghost-001",
      run_id: "run-ghost-001",
      recorded_at_utc: "2099-03-29T12:00:00.000Z",
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
      result: "loss",
      winner_combatant_id: "Opponent",
      loser_combatant_id: "Player",
      player_hand: { items: [] },
      player_skills: { items: [] },
      opponent_hand: { items: [] },
      opponent_skills: { items: [] },
    }),
    created_at_utc: "2099-03-29T12:00:00.000Z",
    updated_at_utc: "2099-03-29T12:00:00.000Z",
  });
  env.DB.pvpBattles.set("battle-ghost-ignored", {
    battle_id: "battle-ghost-ignored",
    run_id: "run-ghost-ignored",
    source_client_id: "remote-client",
    recorded_at_utc: "2099-03-29T11:00:00.000Z",
    day: 9,
    hour: 1,
    encounter_id: "encounter-ghost-ignored",
    player_name: "Uploader",
    player_account_id: "uploader-account",
    player_hero: null,
    player_rank: null,
    player_rating: null,
    player_level: null,
    opponent_name: "Else",
    opponent_account_id: "someone-else",
    opponent_hero: null,
    opponent_rank: null,
    opponent_rating: null,
    opponent_level: null,
    combat_kind: "PVPCombat",
    result: "win",
    winner_combatant_id: "Player",
    loser_combatant_id: "Opponent",
    payload_json: JSON.stringify({ battle_id: "battle-ghost-ignored" }),
    created_at_utc: "2099-03-29T11:00:00.000Z",
    updated_at_utc: "2099-03-29T11:00:00.000Z",
  });
  env.DB.replayUploads.set("battle-ghost-001", {
    battle_id: "battle-ghost-001",
    client_id: "replay-client",
    install_id: "replay-install",
    run_id: "run-ghost-001",
    payload_sha256: "hash",
    object_key: "combat-replays/replays/replay-client/battle-ghost-001/hash.payload.json",
    uploaded_at_utc: "2099-03-29T12:05:00.000Z",
  });

  const response = await worker.fetch(
    buildSignedRequest(
      "GET",
      "https://example.com/me/pvp-battles/against-me?days=3&limit=10",
      clientId,
      "install-ghost-query",
      privateKey,
      "nonce-ghost-query",
    ),
    env as never,
  );

  assert.equal(response.status, 200);
  const body = (await response.json()) as {
    resolved_account_ids: string[];
    battles: Array<{
      battle_id: string;
      player_hero?: string;
      player_level?: number;
      replay?: { available: boolean };
    }>;
  };
  assert.deepEqual(body.resolved_account_ids, ["my-account"]);
  assert.equal(body.battles.length, 1);
  assert.equal(body.battles[0]?.battle_id, "battle-ghost-001");
  assert.equal(body.battles[0]?.player_hero, "Dooley");
  assert.equal(body.battles[0]?.player_level, 11);
  assert.equal(body.battles[0]?.replay?.available, true);
});

test("creates a replay download link and serves the payload", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-ghost-download";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-ghost-download",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });
  env.DB.clientUidBindings.set("binding-ghost-download", {
    binding_id: "binding-ghost-download",
    client_id: clientId,
    uid: "uid-ghost-download",
    bound_at_utc: new Date().toISOString(),
    unbound_at_utc: null,
  });
  env.DB.uidPlayerAccounts.set("uid-ghost-download:my-account", {
    uid: "uid-ghost-download",
    player_account_id: "my-account",
    first_seen_at_utc: "2026-03-28T00:00:00.000Z",
    last_seen_at_utc: "2026-03-29T12:00:00.000Z",
    last_client_id: clientId,
  });
  env.DB.pvpBattles.set("battle-ghost-download", {
    battle_id: "battle-ghost-download",
    run_id: "run-ghost-download",
    source_client_id: "remote-client",
    recorded_at_utc: "2099-03-29T12:00:00.000Z",
    day: 9,
    hour: 2,
    encounter_id: "encounter-ghost-download",
    player_name: "Uploader",
    player_account_id: "uploader-account",
    player_hero: "Stelle",
    player_rank: null,
    player_rating: null,
    player_level: 6,
    opponent_name: "Me",
    opponent_account_id: "my-account",
    opponent_hero: "Mak",
    opponent_rank: null,
    opponent_rating: null,
    opponent_level: null,
    combat_kind: "PVPCombat",
    result: "loss",
    winner_combatant_id: "Opponent",
    loser_combatant_id: "Player",
    payload_json: JSON.stringify({ battle_id: "battle-ghost-download" }),
    created_at_utc: "2099-03-29T12:00:00.000Z",
    updated_at_utc: "2099-03-29T12:00:00.000Z",
  });
  env.DB.replayUploads.set("battle-ghost-download", {
    battle_id: "battle-ghost-download",
    client_id: "replay-client",
    install_id: "replay-install",
    run_id: "run-ghost-download",
    payload_sha256: "hash",
    object_key: "combat-replays/replays/replay-client/battle-ghost-download/hash.payload.json",
    uploaded_at_utc: "2099-03-29T12:05:00.000Z",
  });
  env.REPLAY_BUCKET.objects.set(
    "combat-replays/replays/replay-client/battle-ghost-download/hash.payload.json",
    new TextEncoder().encode("{\"battle_id\":\"battle-ghost-download\"}"),
  );

  const linkResponse = await worker.fetch(
    buildSignedRequest(
      "POST",
      "https://example.com/me/pvp-battles/battle-ghost-download/replay-download-link",
      clientId,
      "install-ghost-download",
      privateKey,
      "nonce-ghost-download-link",
    ),
    env as never,
  );

  assert.equal(linkResponse.status, 200);
  const linkBody = (await linkResponse.json()) as { download_url: string };
  assert.ok(linkBody.download_url.includes("/replays/download?"));

  const downloadResponse = await worker.fetch(
    new Request(linkBody.download_url, { method: "GET" }),
    env as never,
  );

  assert.equal(downloadResponse.status, 200);
  assert.equal(
    await downloadResponse.text(),
    "{\"battle_id\":\"battle-ghost-download\"}",
  );
});
