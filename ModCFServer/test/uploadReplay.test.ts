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

function stringifyConsoleArgs(args: unknown[]): string {
  return args
    .map((value) =>
      typeof value === "string" ? value : JSON.stringify(value),
    )
    .join(" ");
}

async function captureWarnLogs<T>(
  run: () => Promise<T>,
): Promise<{ result: T; entries: string[] }> {
  const entries: string[] = [];
  const originalWarn = console.warn;
  console.warn = (...args: unknown[]) => {
    entries.push(stringifyConsoleArgs(args));
  };

  try {
    const result = await run();
    return { result, entries };
  } finally {
    console.warn = originalWarn;
  }
}

async function captureInfoLogs<T>(
  run: () => Promise<T>,
): Promise<{ result: T; entries: string[] }> {
  const entries: string[] = [];
  const originalInfo = console.info;
  console.info = (...args: unknown[]) => {
    entries.push(stringifyConsoleArgs(args));
  };

  try {
    const result = await run();
    return { result, entries };
  } finally {
    console.info = originalInfo;
  }
}

test("persists verified battle uploads", async () => {
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
    replay_available: 0,
    created_at_utc: "2026-03-29T12:00:00.000Z",
    updated_at_utc: "2026-03-29T12:00:00.000Z",
  });

  const payload = JSON.stringify({
    battle_id: "battle-001",
    client_id: registerJson.client_id,
    battle_manifest: {
      battle_id: "battle-001",
      run_id: "run-001",
      recorded_at_utc: "2026-03-29T12:00:00.000Z",
      day: 8,
      hour: 1,
      encounter_id: "encounter-001",
      combat_kind: "PVPCombat",
      participants: {
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
      },
      outcome: {
        result: "win",
        winner_combatant_id: "Player",
        loser_combatant_id: "Opponent",
      },
      snapshots: {
        player_hand: { items: [] },
        player_skills: { items: [] },
        opponent_hand: { items: [] },
        opponent_skills: { items: [] },
      },
    },
    replay_payload: {
      battle_id: "battle-001",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });
  const bodyHash = sha256Base64(payload);
  const storedBattlePayload = JSON.stringify({
    battle_id: "battle-001",
    battle_manifest: {
      battle_id: "battle-001",
      run_id: "run-001",
      recorded_at_utc: "2026-03-29T12:00:00.000Z",
      day: 8,
      hour: 1,
      encounter_id: "encounter-001",
      combat_kind: "PVPCombat",
      participants: {
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
      },
      outcome: {
        result: "win",
        winner_combatant_id: "Player",
        loser_combatant_id: "Opponent",
      },
      snapshots: {
        player_hand: { items: [] },
        player_skills: { items: [] },
        opponent_hand: { items: [] },
        opponent_skills: { items: [] },
      },
    },
    replay_payload: {
      battle_id: "battle-001",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });
  const storedBattlePayloadHash = sha256Base64(storedBattlePayload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-001";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/battles/upload",
      registerJson.client_id,
      "install-001",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/battles/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": registerJson.client_id,
        "x-bpp-install-id": "install-001",
        "x-bpp-battle-id": "battle-001",
        "x-bpp-plugin-version": "1.9.0",
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
  const json = (await response.json()) as { object_key: string };
  assert.equal(
    json.object_key,
    `replays/${registerJson.client_id}/battle-001/${storedBattlePayloadHash}.json`,
  );
  const projectedBattle = env.DB.pvpBattles.get("battle-001");
  assert.equal(projectedBattle?.replay_object_key, json.object_key);
  assert.ok((projectedBattle?.replay_uploaded_at_utc ?? "").length > 0);
  assert.equal(projectedBattle?.replay_available, 1);
  assert.ok(env.PVP_BATTLE_BUCKET.objects.has(json.object_key));
});

test("successful battle uploads do not emit accepted info logs", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();

  const registerResponse = await worker.fetch(
    new Request("https://example.com/clients/register", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        install_id: "install-battle-no-info-log",
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

  const payload = JSON.stringify({
    battle_id: "battle-no-info-log",
    client_id: registerJson.client_id,
    battle_manifest: {
      battle_id: "battle-no-info-log",
      run_id: "run-no-info-log",
      recorded_at_utc: "2026-03-29T12:00:00.000Z",
      combat_kind: "PVPCombat",
      participants: {
        player_name: "Uploader",
        player_account_id: "uploader-account",
        opponent_name: "Me",
        opponent_account_id: "my-account",
      },
      outcome: {},
      snapshots: {
        player_hand: { items: [] },
        player_skills: { items: [] },
        opponent_hand: { items: [] },
        opponent_skills: { items: [] },
      },
    },
    replay_payload: {
      battle_id: "battle-no-info-log",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/battles/upload",
      registerJson.client_id,
      "install-battle-no-info-log",
      timestamp,
      "nonce-battle-no-info-log",
      bodyHash,
    ),
  );

  const { result: response, entries } = await captureInfoLogs(() =>
    worker.fetch(
      new Request("https://example.com/battles/upload", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "x-bpp-client-id": registerJson.client_id,
          "x-bpp-install-id": "install-battle-no-info-log",
          "x-bpp-battle-id": "battle-no-info-log",
          "x-bpp-plugin-version": "1.9.0",
          "x-bpp-timestamp": timestamp,
          "x-bpp-content-sha256": bodyHash,
          "x-bpp-signature-alg": "rsa-pkcs1-sha256",
          "x-bpp-signature": signature,
        },
        body: payload,
      }),
      env as never,
    ),
  );

  assert.equal(response.status, 200);
  assert.equal(
    entries.some((entry) => entry.includes("\"event\":\"battle_upload.accepted\"")),
    false,
  );
});

test("rejects battle uploads when top-level and manifest run ids disagree", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "replays-client-runid-mismatch";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-runid-mismatch",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    battle_id: "battle-runid-mismatch",
    run_id: "run-top",
    battle_manifest: {
      battle_id: "battle-runid-mismatch",
      run_id: "run-manifest",
      recorded_at_utc: "2026-03-29T12:00:00.000Z",
      combat_kind: "PVPCombat",
      participants: {
        opponent_account_id: "my-account",
      },
      outcome: {},
      snapshots: {
        player_hand: { items: [] },
        player_skills: { items: [] },
        opponent_hand: { items: [] },
        opponent_skills: { items: [] },
      },
    },
    replay_payload: {
      battle_id: "battle-runid-mismatch",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-runid-mismatch";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/battles/upload",
      clientId,
      "install-runid-mismatch",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/battles/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-runid-mismatch",
        "x-bpp-battle-id": "battle-runid-mismatch",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "run_id_mismatch" });
  assert.equal(env.DB.pvpBattles.size, 0);
});

test("rejects battle uploads when x-bpp-run-id disagrees with the payload", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "replays-client-header-runid-mismatch";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-header-runid-mismatch",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    battle_id: "battle-header-runid-mismatch",
    run_id: "run-payload",
    battle_manifest: {
      battle_id: "battle-header-runid-mismatch",
      run_id: "run-payload",
      recorded_at_utc: "2026-03-29T12:00:00.000Z",
      combat_kind: "PVPCombat",
      participants: {
        opponent_account_id: "my-account",
      },
      outcome: {},
      snapshots: {
        player_hand: { items: [] },
        player_skills: { items: [] },
        opponent_hand: { items: [] },
        opponent_skills: { items: [] },
      },
    },
    replay_payload: {
      battle_id: "battle-header-runid-mismatch",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-header-runid-mismatch";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/battles/upload",
      clientId,
      "install-header-runid-mismatch",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/battles/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-header-runid-mismatch",
        "x-bpp-battle-id": "battle-header-runid-mismatch",
        "x-bpp-run-id": "run-header",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "run_id_mismatch" });
  assert.equal(env.DB.pvpBattles.size, 0);
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
    battle_manifest: {
      battle_id: "battle-repeat",
      recorded_at_utc: "2026-03-29T12:00:00.000Z",
      combat_kind: "PVPCombat",
      participants: {
        opponent_account_id: "my-account",
      },
      outcome: {},
      snapshots: {
        player_hand: { items: [] },
        player_skills: { items: [] },
        opponent_hand: { items: [] },
        opponent_skills: { items: [] },
      },
    },
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
        "/battles/upload",
        clientId,
        "install-repeat-upload",
        timestamp,
        nonce,
        bodyHash,
      ),
    );

    const response = await worker.fetch(
      new Request("https://example.com/battles/upload", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "x-bpp-client-id": clientId,
          "x-bpp-install-id": "install-repeat-upload",
          "x-bpp-battle-id": "battle-repeat",
          "x-bpp-plugin-version": "1.9.0",
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
  }

  assert.equal(env.PVP_BATTLE_BUCKET.objects.size, 1);
  assert.equal(env.DB.pvpBattles.get("battle-repeat")?.replay_available, 1);
});

test("rejects battle uploads with incomplete replay payloads", async () => {
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
    battle_manifest: {
      battle_id: "battle-invalid",
      recorded_at_utc: "2026-03-29T12:00:00.000Z",
      combat_kind: "PVPCombat",
      participants: {
        opponent_account_id: "my-account",
      },
      outcome: {},
      snapshots: {
        player_hand: { items: [] },
        player_skills: { items: [] },
        opponent_hand: { items: [] },
        opponent_skills: { items: [] },
      },
    },
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
      "/battles/upload",
      clientId,
      "install-invalid-replay",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const response = await worker.fetch(
    new Request("https://example.com/battles/upload", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        "x-bpp-client-id": clientId,
        "x-bpp-install-id": "install-invalid-replay",
        "x-bpp-battle-id": "battle-invalid",
        "x-bpp-plugin-version": "1.9.0",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature-alg": "rsa-pkcs1-sha256",
        "x-bpp-signature": signature,
      },
      body: payload,
    }),
    env as never,
  );

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), {
    error: "invalid_replay_payload",
    reason: "missing_despawn_message_base64",
  });
  assert.equal(env.PVP_BATTLE_BUCKET.objects.size, 0);
});

test("logs battle manifest validation reasons for rejected uploads", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "replays-client-missing-recorded-at";
  env.DB.clients.set(clientId, {
    client_id: clientId,
    install_id: "install-missing-recorded-at",
    purpose: "replays",
    modulus_b64: modulusB64,
    exponent_b64: exponentB64,
    plugin_version: "1.9.0",
    registered_at_utc: new Date().toISOString(),
  });

  const payload = JSON.stringify({
    battle_id: "battle-missing-recorded-at",
    battle_manifest: {
      battle_id: "battle-missing-recorded-at",
      combat_kind: "PVPCombat",
      participants: {
        opponent_account_id: "my-account",
      },
      outcome: {},
      snapshots: {
        player_hand: { items: [] },
        player_skills: { items: [] },
        opponent_hand: { items: [] },
        opponent_skills: { items: [] },
      },
    },
    replay_payload: {
      battle_id: "battle-missing-recorded-at",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });
  const bodyHash = sha256Base64(payload);
  const timestamp = new Date().toISOString();
  const nonce = "nonce-missing-recorded-at";
  const signature = signCanonical(
    privateKey,
    canonicalRequest(
      "POST",
      "/battles/upload",
      clientId,
      "install-missing-recorded-at",
      timestamp,
      nonce,
      bodyHash,
    ),
  );

  const { result: response, entries } = await captureWarnLogs(() =>
    worker.fetch(
      new Request("https://example.com/battles/upload", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "x-bpp-client-id": clientId,
          "x-bpp-install-id": "install-missing-recorded-at",
          "x-bpp-battle-id": "battle-missing-recorded-at",
          "x-bpp-plugin-version": "1.9.0",
          "x-bpp-timestamp": timestamp,
          "x-bpp-content-sha256": bodyHash,
          "x-bpp-signature-alg": "rsa-pkcs1-sha256",
          "x-bpp-signature": signature,
        },
        body: payload,
      }),
      env as never,
    ),
  );

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), {
    error: "invalid_battle_manifest",
    reason: "missing_recorded_at_utc",
  });

  const rejectionLog = entries.find((entry) =>
    entry.includes("\"event\":\"battle_upload.rejected\""),
  );
  assert.ok(rejectionLog, "Rejected battle uploads should emit a structured warning log.");
  assert.match(rejectionLog, /"error_code":"invalid_battle_manifest"/);
  assert.match(rejectionLog, /"validation_reason":"missing_recorded_at_utc"/);
  assert.match(rejectionLog, /"battle_id":"battle-missing-recorded-at"/);
});
