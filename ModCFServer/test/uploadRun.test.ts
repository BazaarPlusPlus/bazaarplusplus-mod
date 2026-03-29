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

function buildSignedJsonRequest(
  url: string,
  clientId: string,
  installId: string,
  privateKey: Parameters<typeof signCanonical>[0],
  nonce: string,
  payload: string,
  extraHeaders?: Record<string, string>,
): Request {
  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64(payload);
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      new URL(url).pathname,
      clientId,
      installId,
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  return new Request(url, {
    method: "POST",
    headers: {
      "content-type": "application/json",
      "x-bpp-client-id": clientId,
      "x-bpp-install-id": installId,
      "x-bpp-timestamp": timestamp,
      "x-bpp-nonce": nonce,
      "x-bpp-content-sha256": bodyHash,
      "x-bpp-signature-alg": "rsa-pkcs1-sha256",
      "x-bpp-signature": signature,
      ...(extraHeaders ?? {}),
    },
    body: payload,
  });
}

async function bindClientToPlayerAccount(
  env: ReturnType<typeof buildEnv>,
  clientId: string,
  installId: string,
  privateKey: Parameters<typeof signCanonical>[0],
  playerAccountId: string,
  nonce: string,
): Promise<void> {
  const response = await worker.fetch(
    buildSignedJsonRequest(
      "https://example.com/clients/bind",
      clientId,
      installId,
      privateKey,
      nonce,
      JSON.stringify({
        player_account_id: playerAccountId,
        observed_player_account_id: playerAccountId,
      }),
    ),
    env as never,
  );

  assert.equal(response.status, 200);
}

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

  const payload = JSON.stringify({
    run_id: "run-001",
    schema_version: 3,
    state: "active",
    pvp_battles: [],
  });
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
  const uploadedRun = env.DB.runUploads.get("run-001");
  assert.equal(uploadedRun?.client_id, clientId);
  assert.equal(uploadedRun?.projection_status, "projected");
  assert.equal(
    uploadedRun?.payload_object_key,
    `runs/${clientId}/run-001/${bodyHash}.json`,
  );
  assert.equal(uploadedRun?.payload_bytes, new TextEncoder().encode(payload).byteLength);
  assert.equal(uploadedRun?.schema_version, 3);
  assert.equal(uploadedRun?.projection_version, 1);
  assert.equal(uploadedRun?.projected_battle_count, 0);
  assert.ok(env.REPLAY_BUCKET.objects.has(`runs/${clientId}/run-001/${bodyHash}.json`));
});

test("projects uploaded pvp battles for a bound player account client", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-projection";
  const installId = "install-run-projection";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: installId,
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });
  await bindClientToPlayerAccount(
    env,
    clientId,
    installId,
    privateKey,
    "player-account-001",
    "nonce-bind-run-projection",
  );

  const payload = JSON.stringify({
    run_id: "run-projection",
    schema_version: 5,
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
        debug_extra: "drop-me",
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
      installId,
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
        "x-bpp-install-id": installId,
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
  assert.equal(projectedBattle?.replay_available, 0);
  const projectedSummary = JSON.parse(projectedBattle?.summary_json ?? "{}") as Record<
    string,
    unknown
  >;
  assert.equal(projectedSummary.player_hero, "Dooley");
  assert.deepEqual(projectedSummary.player_hand, { items: [] });
  assert.deepEqual(projectedSummary.opponent_skills, { items: [] });
  assert.equal("debug_extra" in projectedSummary, false);
  assert.equal(env.DB.runUploads.get("run-projection")?.projection_status, "projected");
  assert.equal(env.DB.runUploads.get("run-projection")?.projected_battle_count, 1);
  assert.equal(env.DB.runUploads.get("run-projection")?.projection_version, 1);
});

test("records a failed projection in the run ingestion ledger", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-projection-failure";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-projection-failure",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });
  env.DB.batch = async () => {
    throw new Error("projection-write-failed");
  };

  const payload = JSON.stringify({
    run_id: "run-projection-failure",
    schema_version: 2,
    pvp_battles: [
      {
        battle_id: "battle-projection-failure-001",
        opponent_account_id: "opponent-account-001",
        combat_kind: "PVPCombat",
      },
    ],
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-run-projection-failure";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-projection-failure",
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
        "x-bpp-install-id": "install-run-projection-failure",
        "x-bpp-run-id": "run-projection-failure",
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

  assert.equal(response.status, 500);
  assert.deepEqual(await response.json(), { error: "projection_failed" });
  assert.equal(env.DB.runUploads.get("run-projection-failure")?.projection_status, "failed");
  assert.equal(env.DB.runUploads.get("run-projection-failure")?.last_error_code, "projection_failed");
  assert.match(
    env.DB.runUploads.get("run-projection-failure")?.last_error_detail ?? "",
    /projection-write-failed/,
  );
});

test("run uploads do not depend on legacy uid bindings once the client is bound", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-no-legacy-uid";
  const installId = "install-run-no-legacy-uid";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: installId,
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });
  await bindClientToPlayerAccount(
    env,
    clientId,
    installId,
    privateKey,
    "player-account-new",
    "nonce-bind-no-legacy-uid",
  );
  const originalFirst = env.DB.first.bind(env.DB);
  env.DB.first = ((sql: string, params: unknown[]) => {
    if (sql.includes("FROM client_uid_bindings") || sql.includes("FROM uid_player_accounts")) {
      throw new Error("legacy_uid_lookup_invoked");
    }
    return originalFirst(sql, params);
  }) as typeof env.DB.first;

  const payload = JSON.stringify({
    run_id: "run-no-legacy-uid",
    pvp_battles: [
      {
        battle_id: "battle-no-legacy-uid-001",
        run_id: "run-no-legacy-uid",
        recorded_at_utc: "2026-03-29T10:15:00.000Z",
        player_account_id: "player-account-new",
        opponent_account_id: "opponent-account-001",
        combat_kind: "PVPCombat",
      },
    ],
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-sticky-account";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      installId,
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
        "x-bpp-install-id": installId,
        "x-bpp-run-id": "run-no-legacy-uid",
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
  assert.equal(env.DB.runUploads.get("run-no-legacy-uid")?.projection_status, "projected");
});

test("run uploads do not create legacy uid-player-account observations", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-no-legacy-observations";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-no-legacy-observations",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    run_id: "run-no-legacy-observations",
    pvp_battles: [
      {
        battle_id: "battle-no-legacy-observations-001",
        player_account_id: "player-account-a",
        opponent_account_id: "opponent-account-001",
        combat_kind: "PVPCombat",
      },
      {
        battle_id: "battle-no-legacy-observations-002",
        player_account_id: "player-account-b",
        opponent_account_id: "opponent-account-002",
        combat_kind: "PVPCombat",
      },
    ],
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-ambiguous-account";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-no-legacy-observations",
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
        "x-bpp-install-id": "install-run-no-legacy-observations",
        "x-bpp-run-id": "run-no-legacy-observations",
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
  assert.equal(env.DB.uidPlayerAccounts.size, 0);
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

  const payload = JSON.stringify({
    run_id: "run-repeat",
    state: "active",
    pvp_battles: [],
  });
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

test("rejects run uploads when pvp_battles is missing", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-missing-battles";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-missing-battles",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({ run_id: "run-missing-battles" });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-run-missing-battles";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-missing-battles",
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
        "x-bpp-install-id": "install-run-missing-battles",
        "x-bpp-run-id": "run-missing-battles",
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
  assert.deepEqual(await response.json(), { error: "pvp_battles_required" });
  assert.equal(env.DB.runUploads.size, 0);
  assert.equal(env.DB.pvpBattles.size, 0);
});

test("rejects run uploads with incomplete pvp battle payloads", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "runs-client-invalid-battle";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-run-invalid-battle",
    purpose: "runs",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    run_id: "run-invalid-battle",
    pvp_battles: [
      {
        battle_id: "battle-invalid-001",
        combat_kind: "PVPCombat",
      },
    ],
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-run-invalid-battle";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/runs/upload",
      clientId,
      "install-run-invalid-battle",
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
        "x-bpp-install-id": "install-run-invalid-battle",
        "x-bpp-run-id": "run-invalid-battle",
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
  assert.deepEqual(await response.json(), { error: "invalid_pvp_battle_payload" });
  assert.equal(env.DB.runUploads.size, 0);
  assert.equal(env.DB.pvpBattles.size, 0);
});
