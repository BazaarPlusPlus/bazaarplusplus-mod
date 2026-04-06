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

async function captureInfoLogs<T>(action: () => Promise<T>): Promise<{
  result: T;
  entries: Array<Record<string, unknown>>;
}> {
  const originalInfo = console.info;
  const entries: Array<Record<string, unknown>> = [];
  console.info = ((message?: unknown) => {
    if (typeof message !== "string") {
      return;
    }

    try {
      entries.push(JSON.parse(message) as Record<string, unknown>);
    } catch {
      // Ignore non-JSON log lines.
    }
  }) as typeof console.info;

  try {
    const result = await action();
    return { result, entries };
  } finally {
    console.info = originalInfo;
  }
}

test("stores signed battle artifacts in D1 and compressed replay payloads in R2", async () => {
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
  assert.equal(
    battle?.replay_size_bytes,
    Buffer.byteLength(
      JSON.stringify({
        battle_id: "battle-001",
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
      }),
      "utf8",
    ),
  );

  const storedObject = env.PVP_BATTLE_BUCKET.objects.get(json.object_key);
  assert.ok(storedObject);
  assert.equal(storedObject?.httpMetadata?.contentType, "application/json; charset=utf-8");
  assert.equal(storedObject?.httpMetadata?.contentEncoding, "gzip");
  assert.equal(storedObject?.customMetadata?.["bpp-storage-codec"], "gzip");
  assert.equal(
    storedObject?.customMetadata?.["bpp-compressed-size-bytes"],
    String(storedObject?.body.byteLength),
  );
  assert.notEqual(
    new TextDecoder().decode(storedObject?.body),
    JSON.stringify({
      battle_id: "battle-001",
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
    }),
  );
});

test("silently discards low-mmr battles before day 10", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-battle-discard-001";
  const installId = "install-battle-discard-001";
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
    battle_id: "battle-discard-001",
    run_id: "run-discard-001",
    schema_version: 1,
    battle_manifest: {
      battle_id: "battle-discard-001",
      run_id: "run-discard-001",
      recorded_at_utc: "2026-04-01T00:00:00.000Z",
      day: 9,
      hour: 2,
      combat_kind: "PVPCombat",
      participants: {
        player_name: "Uploader",
        player_account_id: "uploader-account",
        player_hero: "Dooley",
        player_rank: "Legendary 5",
        player_rating: 700,
        player_level: 8,
        opponent_name: "Target",
        opponent_account_id: "target-account",
        opponent_hero: "Vanessa",
        opponent_rank: "Legendary 5",
        opponent_rating: 700,
        opponent_level: 8,
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
      battle_id: "battle-discard-001",
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

  const { result: response, entries } = await captureInfoLogs(() =>
    worker.fetch(
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
    ),
  );

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), {
    status: "accepted",
    discarded: true,
    reason: "low_mmr_before_day_10",
  });
  const discardedLog = entries.find((entry) => entry.event === "battle.discarded");
  assert.ok(discardedLog);
  assert.equal(discardedLog.battle_id, "battle-discard-001");
  assert.equal(discardedLog.run_id, "run-discard-001");
  assert.equal(discardedLog.client_id, clientId);
  assert.equal(discardedLog.day, 9);
  assert.equal(discardedLog.player_rating, 700);
  assert.equal(discardedLog.reason, "low_mmr_before_day_10");
  assert.equal(env.DB.battles.get("battle-discard-001"), undefined);
  assert.equal(env.PVP_BATTLE_BUCKET.objects.size, 0);
});

test("stores high-mmr battles even before day 10", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-battle-accept-002";
  const installId = "install-battle-accept-002";
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
    battle_id: "battle-accept-002",
    run_id: "run-accept-002",
    schema_version: 1,
    battle_manifest: {
      battle_id: "battle-accept-002",
      run_id: "run-accept-002",
      recorded_at_utc: "2026-04-01T00:00:00.000Z",
      day: 3,
      hour: 2,
      combat_kind: "PVPCombat",
      participants: {
        player_name: "Uploader",
        player_account_id: "uploader-account",
        player_hero: "Dooley",
        player_rank: "Legendary 5",
        player_rating: 701,
        player_level: 8,
        opponent_name: "Target",
        opponent_account_id: "target-account",
        opponent_hero: "Vanessa",
        opponent_rank: "Legendary 5",
        opponent_rating: 650,
        opponent_level: 8,
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
      battle_id: "battle-accept-002",
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

  const { result: response, entries } = await captureInfoLogs(() =>
    worker.fetch(
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
    ),
  );

  assert.equal(response.status, 200);
  const json = (await response.json()) as { status: string; object_key: string };
  assert.equal(json.status, "accepted");
  assert.ok(json.object_key);
  assert.ok(env.DB.battles.get("battle-accept-002"));
  assert.equal(env.PVP_BATTLE_BUCKET.objects.size, 1);
});

test("stores low-mmr battles starting on day 10", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-battle-accept-003";
  const installId = "install-battle-accept-003";
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
    battle_id: "battle-accept-003",
    run_id: "run-accept-003",
    schema_version: 1,
    battle_manifest: {
      battle_id: "battle-accept-003",
      run_id: "run-accept-003",
      recorded_at_utc: "2026-04-01T00:00:00.000Z",
      day: 10,
      hour: 2,
      combat_kind: "PVPCombat",
      participants: {
        player_name: "Uploader",
        player_account_id: "uploader-account",
        player_hero: "Dooley",
        player_rank: "Gold",
        player_rating: 650,
        player_level: 8,
        opponent_name: "Target",
        opponent_account_id: "target-account",
        opponent_hero: "Vanessa",
        opponent_rank: "Gold",
        opponent_rating: 640,
        opponent_level: 8,
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
      battle_id: "battle-accept-003",
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
  assert.ok(env.DB.battles.get("battle-accept-003"));
  assert.equal(env.PVP_BATTLE_BUCKET.objects.size, 1);
});

test("downloads both legacy and compressed replay objects as plain json", async () => {
  const env = buildEnv();
  const replayJson = JSON.stringify({
    battle_id: "battle-download-001",
    battle_manifest: {
      battle_id: "battle-download-001",
      recorded_at_utc: "2026-04-01T00:00:00.000Z",
      combat_kind: "PVPCombat",
      participants: {
        opponent_account_id: "target-account",
      },
      snapshots: {},
    },
    replay_payload: {
      battle_id: "battle-download-001",
      version: 1,
      spawn_message_base64: "c3Bhd24=",
      combat_message_base64: "Y29tYmF0",
      despawn_message_base64: "ZGVzcGF3bg==",
    },
  });

  env.DB.replayTokens.set("token-legacy", {
    token: "token-legacy",
    battle_id: "battle-legacy-001",
    requested_by_player_account_id: "requester",
    expires_at_utc: "2099-04-01T00:00:00.000Z",
    created_at_utc: "2026-04-01T00:00:00.000Z",
    used_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.battles.set("battle-legacy-001", {
    battle_id: "battle-legacy-001",
    run_id: null,
    client_id: "client-legacy",
    uploader_player_account_id: null,
    recorded_at_utc: "2026-04-01T00:00:00.000Z",
    day: null,
    hour: null,
    player_name: null,
    player_account_id: null,
    player_hero: null,
    player_rank: null,
    player_rating: null,
    player_level: null,
    opponent_name: null,
    opponent_account_id: null,
    opponent_hero: null,
    opponent_rank: null,
    opponent_rating: null,
    opponent_level: null,
    combat_kind: "PVPCombat",
    result: null,
    winner_combatant_id: null,
    loser_combatant_id: null,
    replay_schema_version: 1,
    replay_object_key: "battle-replays/client-legacy/battle-legacy-001/hash.json",
    replay_size_bytes: Buffer.byteLength(replayJson, "utf8"),
    created_at_utc: "2026-04-01T00:00:00.000Z",
    updated_at_utc: "2026-04-01T00:00:00.000Z",
  });
  await env.PVP_BATTLE_BUCKET.put(
    "battle-replays/client-legacy/battle-legacy-001/hash.json",
    new TextEncoder().encode(replayJson),
    {
      httpMetadata: {
        contentType: "application/json; charset=utf-8",
      },
    },
  );

  env.DB.replayTokens.set("token-compressed", {
    token: "token-compressed",
    battle_id: "battle-compressed-001",
    requested_by_player_account_id: "requester",
    expires_at_utc: "2099-04-01T00:00:00.000Z",
    created_at_utc: "2026-04-01T00:00:00.000Z",
    used_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.battles.set("battle-compressed-001", {
    battle_id: "battle-compressed-001",
    run_id: null,
    client_id: "client-compressed",
    uploader_player_account_id: null,
    recorded_at_utc: "2026-04-01T00:00:00.000Z",
    day: null,
    hour: null,
    player_name: null,
    player_account_id: null,
    player_hero: null,
    player_rank: null,
    player_rating: null,
    player_level: null,
    opponent_name: null,
    opponent_account_id: null,
    opponent_hero: null,
    opponent_rank: null,
    opponent_rating: null,
    opponent_level: null,
    combat_kind: "PVPCombat",
    result: null,
    winner_combatant_id: null,
    loser_combatant_id: null,
    replay_schema_version: 1,
    replay_object_key: "battle-replays/client-compressed/battle-compressed-001/hash.json",
    replay_size_bytes: Buffer.byteLength(replayJson, "utf8"),
    created_at_utc: "2026-04-01T00:00:00.000Z",
    updated_at_utc: "2026-04-01T00:00:00.000Z",
  });
  const compressionStream = new CompressionStream("gzip");
  const compressionWriter = compressionStream.writable.getWriter();
  await compressionWriter.write(new TextEncoder().encode(replayJson));
  await compressionWriter.close();
  const compressedReplay = await new Response(compressionStream.readable).arrayBuffer();
  await env.PVP_BATTLE_BUCKET.put(
    "battle-replays/client-compressed/battle-compressed-001/hash.json",
    compressedReplay,
    {
      httpMetadata: {
        contentType: "application/json; charset=utf-8",
        contentEncoding: "gzip",
      },
      customMetadata: {
        "bpp-storage-codec": "gzip",
        "bpp-compressed-size-bytes": String(compressedReplay.byteLength),
      },
    },
  );

  const legacyResponse = await worker.fetch(
    new Request("https://example.com/replays/token-legacy", {
      method: "GET",
    }),
    env as never,
  );
  assert.equal(legacyResponse.status, 200);
  assert.equal(
    legacyResponse.headers.get("content-type"),
    "application/json; charset=utf-8",
  );
  assert.equal(legacyResponse.headers.get("content-encoding"), null);
  assert.equal(await legacyResponse.text(), replayJson);

  const compressedResponse = await worker.fetch(
    new Request("https://example.com/replays/token-compressed", {
      method: "GET",
    }),
    env as never,
  );
  assert.equal(compressedResponse.status, 200);
  assert.equal(
    compressedResponse.headers.get("content-type"),
    "application/json; charset=utf-8",
  );
  assert.equal(compressedResponse.headers.get("content-encoding"), null);
  assert.equal(await compressedResponse.text(), replayJson);
});

test("returns replay_decode_failed for corrupt compressed replay objects", async () => {
  const env = buildEnv();

  env.DB.replayTokens.set("token-corrupt", {
    token: "token-corrupt",
    battle_id: "battle-corrupt-001",
    requested_by_player_account_id: "requester",
    expires_at_utc: "2099-04-01T00:00:00.000Z",
    created_at_utc: "2026-04-01T00:00:00.000Z",
    used_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.battles.set("battle-corrupt-001", {
    battle_id: "battle-corrupt-001",
    run_id: null,
    client_id: "client-corrupt",
    uploader_player_account_id: null,
    recorded_at_utc: "2026-04-01T00:00:00.000Z",
    day: null,
    hour: null,
    player_name: null,
    player_account_id: null,
    player_hero: null,
    player_rank: null,
    player_rating: null,
    player_level: null,
    opponent_name: null,
    opponent_account_id: null,
    opponent_hero: null,
    opponent_rank: null,
    opponent_rating: null,
    opponent_level: null,
    combat_kind: "PVPCombat",
    result: null,
    winner_combatant_id: null,
    loser_combatant_id: null,
    replay_schema_version: 1,
    replay_object_key: "battle-replays/client-corrupt/battle-corrupt-001/hash.json",
    replay_size_bytes: 123,
    created_at_utc: "2026-04-01T00:00:00.000Z",
    updated_at_utc: "2026-04-01T00:00:00.000Z",
  });
  await env.PVP_BATTLE_BUCKET.put(
    "battle-replays/client-corrupt/battle-corrupt-001/hash.json",
    new TextEncoder().encode("not-gzip-data"),
    {
      httpMetadata: {
        contentType: "application/json; charset=utf-8",
        contentEncoding: "gzip",
      },
      customMetadata: {
        "bpp-storage-codec": "gzip",
        "bpp-compressed-size-bytes": "13",
      },
    },
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-corrupt", {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 502);
  assert.deepEqual(await response.json(), { error: "replay_decode_failed" });
});

test("returns replay_decode_failed for replay objects with unsupported declared codec", async () => {
  const env = buildEnv();

  env.DB.replayTokens.set("token-unsupported-codec", {
    token: "token-unsupported-codec",
    battle_id: "battle-unsupported-codec-001",
    requested_by_player_account_id: "requester",
    expires_at_utc: "2099-04-01T00:00:00.000Z",
    created_at_utc: "2026-04-01T00:00:00.000Z",
    used_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.battles.set("battle-unsupported-codec-001", {
    battle_id: "battle-unsupported-codec-001",
    run_id: null,
    client_id: "client-unsupported-codec",
    uploader_player_account_id: null,
    recorded_at_utc: "2026-04-01T00:00:00.000Z",
    day: null,
    hour: null,
    player_name: null,
    player_account_id: null,
    player_hero: null,
    player_rank: null,
    player_rating: null,
    player_level: null,
    opponent_name: null,
    opponent_account_id: null,
    opponent_hero: null,
    opponent_rank: null,
    opponent_rating: null,
    opponent_level: null,
    combat_kind: "PVPCombat",
    result: null,
    winner_combatant_id: null,
    loser_combatant_id: null,
    replay_schema_version: 1,
    replay_object_key:
      "battle-replays/client-unsupported-codec/battle-unsupported-codec-001/hash.json",
    replay_size_bytes: 123,
    created_at_utc: "2026-04-01T00:00:00.000Z",
    updated_at_utc: "2026-04-01T00:00:00.000Z",
  });
  await env.PVP_BATTLE_BUCKET.put(
    "battle-replays/client-unsupported-codec/battle-unsupported-codec-001/hash.json",
    new TextEncoder().encode("not-json"),
    {
      httpMetadata: {
        contentType: "application/json; charset=utf-8",
      },
      customMetadata: {
        "bpp-storage-codec": "brotli",
        "bpp-compressed-size-bytes": "8",
      },
    },
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-unsupported-codec", {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 502);
  assert.deepEqual(await response.json(), { error: "replay_decode_failed" });
});

test("returns replay_read_failed when replay object body cannot be read", async () => {
  const env = buildEnv();

  env.DB.replayTokens.set("token-read-failure", {
    token: "token-read-failure",
    battle_id: "battle-read-failure-001",
    requested_by_player_account_id: "requester",
    expires_at_utc: "2099-04-01T00:00:00.000Z",
    created_at_utc: "2026-04-01T00:00:00.000Z",
    used_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.battles.set("battle-read-failure-001", {
    battle_id: "battle-read-failure-001",
    run_id: null,
    client_id: "client-read-failure",
    uploader_player_account_id: null,
    recorded_at_utc: "2026-04-01T00:00:00.000Z",
    day: null,
    hour: null,
    player_name: null,
    player_account_id: null,
    player_hero: null,
    player_rank: null,
    player_rating: null,
    player_level: null,
    opponent_name: null,
    opponent_account_id: null,
    opponent_hero: null,
    opponent_rank: null,
    opponent_rating: null,
    opponent_level: null,
    combat_kind: "PVPCombat",
    result: null,
    winner_combatant_id: null,
    loser_combatant_id: null,
    replay_schema_version: 1,
    replay_object_key:
      "battle-replays/client-read-failure/battle-read-failure-001/hash.json",
    replay_size_bytes: 123,
    created_at_utc: "2026-04-01T00:00:00.000Z",
    updated_at_utc: "2026-04-01T00:00:00.000Z",
  });
  await env.PVP_BATTLE_BUCKET.put(
    "battle-replays/client-read-failure/battle-read-failure-001/hash.json",
    new TextEncoder().encode("ignored"),
    {
      httpMetadata: {
        contentType: "application/json; charset=utf-8",
      },
      readError: new Error("simulated-r2-read-failure"),
    },
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-read-failure", {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 502);
  assert.deepEqual(await response.json(), { error: "replay_read_failed" });
});

test("returns invalid_json_body when battle id header is missing and body is invalid json", async () => {
  const env = buildEnv();
  const { privateKey, modulusB64, exponentB64 } = generateClientKeyPair();
  const clientId = "client-invalid-json";
  const installId = "install-invalid-json";
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

  const payload = "{";
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

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "invalid_json_body" });
});
