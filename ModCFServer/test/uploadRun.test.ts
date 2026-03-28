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

test("accepts signed run uploads", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-001";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-001",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ run_id: "run-001", state: "active" });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-run-001";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-001",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/runs/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-run-001",
        "x-bpp-run-id": "run-001",
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
  assert.deepEqual(await response.json(), { status: "accepted" });
  assert.equal(env.DB.runUploads.get("run-001")?.client_id, clientId);
  assert.equal(env.DB.runUploads.get("run-001")?.projection_status, "projected");
});

test("projects uploaded pvp battles and records bound player accounts", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-projection";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-projection",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });
  env.DB.clientUidBindings.set("binding-001", {
    binding_id: "binding-001",
    client_id: clientId,
    uid: "uid-001",
    bound_at_utc: new Date().toISOString(),
    unbound_at_utc: null,
  });

  const payload = JSON.stringify({
    run_id: "run-projection",
    pvp_battles: [
      {
        battle_id: "battle-projection-001",
        run_id: "run-projection",
        recorded_at_utc: "2026-03-29T10:15:00.000Z",
        day: 8,
        hour: 2,
        encounter_id: "encounter-001",
        player_name: "Winner",
        player_account_id: "player-account-001",
        player_hero: "Dooley",
        player_rank: "Gold 2",
        player_rating: 1420,
        player_level: 8,
        opponent_name: "Opponent",
        opponent_account_id: "opponent-account-001",
        opponent_hero: "Vanessa",
        opponent_rank: "Gold",
        opponent_rating: 1337,
        opponent_level: 9,
        combat_kind: "PVPCombat",
        result: "win",
        winner_combatant_id: "Player",
        loser_combatant_id: "Opponent",
        player_hand: { items: [] },
        player_skills: { items: [] },
        opponent_hand: { items: [] },
        opponent_skills: { items: [] },
      },
    ],
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-run-projection";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-projection",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/runs/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-run-projection",
        "x-bpp-run-id": "run-projection",
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
  const projectedBattle = env.DB.pvpBattles.get("battle-projection-001");
  assert.equal(projectedBattle?.source_client_id, clientId);
  assert.equal(projectedBattle?.combat_kind, "PVPCombat");
  assert.equal(projectedBattle?.player_account_id, "player-account-001");
  assert.equal(projectedBattle?.player_hero, "Dooley");
  assert.equal(projectedBattle?.player_level, 8);
  assert.equal(env.DB.runUploads.get("run-projection")?.projection_status, "projected");
  assert.equal(
    env.DB.uidPlayerAccounts.get("uid-001:player-account-001")?.last_client_id,
    clientId,
  );
});

test("skips non-pvp battles during run projection", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-nonpvp";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-nonpvp",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    run_id: "run-nonpvp",
    pvp_battles: [
      {
        battle_id: "battle-nonpvp-001",
        recorded_at_utc: "2026-03-29T10:15:00.000Z",
        player_account_id: "player-account-001",
        opponent_account_id: "opponent-account-001",
        combat_kind: "NPCCombat",
        result: "win",
      },
    ],
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-run-nonpvp";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-nonpvp",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/runs/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-run-nonpvp",
        "x-bpp-run-id": "run-nonpvp",
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
  assert.equal(env.DB.pvpBattles.size, 0);
  assert.equal(env.DB.runUploads.get("run-nonpvp")?.projection_status, "projected");
});

test("re-uploading a run replaces projected battles for that run", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-reconcile";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-reconcile",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const firstPayload = JSON.stringify({
    run_id: "run-reconcile",
    pvp_battles: [
      {
        battle_id: "battle-reconcile-001",
        combat_kind: "PVPCombat",
        player_account_id: "player-a",
        opponent_account_id: "player-b",
      },
    ],
  });
  const firstHash = sha256Base64(firstPayload);
  const firstTimestamp = new Date().toISOString();
  const firstSignature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-reconcile",
      firstTimestamp,
      "nonce-run-reconcile-1",
      firstHash,
    ),
  );

  const firstResponse = await worker.fetch(
    new Request("https://example.com/runs/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-run-reconcile",
        "x-bpp-run-id": "run-reconcile",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": firstTimestamp,
        "x-bpp-nonce": "nonce-run-reconcile-1",
        "x-bpp-content-sha256": firstHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": firstSignature,
      },
      body: firstPayload,
    }),
    env as never,
  );
  assert.equal(firstResponse.status, 200);
  assert.equal(env.DB.pvpBattles.size, 1);

  const secondPayload = JSON.stringify({
    run_id: "run-reconcile",
    pvp_battles: [],
  });
  const secondHash = sha256Base64(secondPayload);
  const secondTimestamp = new Date().toISOString();
  const secondSignature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-reconcile",
      secondTimestamp,
      "nonce-run-reconcile-2",
      secondHash,
    ),
  );

  const secondResponse = await worker.fetch(
    new Request("https://example.com/runs/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-run-reconcile",
        "x-bpp-run-id": "run-reconcile",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": secondTimestamp,
        "x-bpp-nonce": "nonce-run-reconcile-2",
        "x-bpp-content-sha256": secondHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": secondSignature,
      },
      body: secondPayload,
    }),
    env as never,
  );
  assert.equal(secondResponse.status, 200);
  assert.equal(env.DB.pvpBattles.size, 0);
});

test("rejects run uploads when header and body run ids disagree", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-mismatch";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-mismatch",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ run_id: "run-body-mismatch", pvp_battles: [] });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-run-mismatch";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-mismatch",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/runs/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-run-mismatch",
        "x-bpp-run-id": "run-header-mismatch",
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
  assert.equal(env.DB.runUploads.size, 0);
});

test("re-uploading the same run payload remains idempotent", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-repeat";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-repeat",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ run_id: "run-repeat", state: "active" });
  const bodyHash = sha256Base64(payload);

  for (const nonce of ["nonce-run-repeat-1", "nonce-run-repeat-2"]) {
    const timestamp = new Date().toISOString();
    const signature = signCanonical(
      privateKey,
      canonicalRequest(
        "POST",
        "/runs/upload",
        clientId,
        "install-run-repeat",
        timestamp,
        nonce,
        bodyHash,
      ),
    );

    const response = await worker.fetch(
      new Request("https://example.com/runs/upload", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "x-bpp-client-id": clientId,
          "x-bpp-install-id": "install-run-repeat",
          "x-bpp-run-id": "run-repeat",
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

  assert.equal(env.DB.runUploads.size, 1);
  assert.equal(env.DB.runUploads.get("run-repeat")?.projection_status, "projected");
});
