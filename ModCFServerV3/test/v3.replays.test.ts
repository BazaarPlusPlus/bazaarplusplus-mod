import assert from "node:assert/strict";
import test from "node:test";

import worker from "../src/index";
import type { MockD1Database } from "./helpers/mockEnv";
import {
  canonicalRequestV3,
  generateClientKeyPair,
  sha256Base64,
  signCanonical,
} from "./helpers/crypto";
import { buildEnv } from "./helpers/mockEnv";

async function insertToken(
  db: MockD1Database,
  token: string,
  playerAccountId: string,
  issuedAtUtc: string,
  revokedAtUtc?: string,
): Promise<void> {
  if (revokedAtUtc == null) {
    await db.prepare(
      `INSERT INTO tokens (token, player_account_id, issued_at_utc) VALUES (?, ?, ?)`,
    )
      .bind(token, playerAccountId, issuedAtUtc)
      .run();
    return;
  }

  await db.prepare(
    `INSERT INTO tokens (token, player_account_id, issued_at_utc, revoked_at_utc) VALUES (?, ?, ?, ?)`,
  )
    .bind(token, playerAccountId, issuedAtUtc, revokedAtUtc)
    .run();
}

test("replay-link returns 401 when Authorization header is missing", async () => {
  const env = buildEnv();

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-no-auth/replay-link", {
      method: "POST",
    }),
    env as never,
  );

  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "invalid_token" });
});

test("replay-link returns 401 when bearer is unknown or revoked", async () => {
  const env = buildEnv();
  await insertToken(
    env.DB,
    "tok-revoked",
    "player-account-001",
    "2026-01-01T00:00:00Z",
    "2026-01-02T00:00:00Z",
  );

  const unknownResponse = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-foreign/replay-link", {
      method: "POST",
      headers: { Authorization: "Bearer tok-unknown" },
    }),
    env as never,
  );

  assert.equal(unknownResponse.status, 401);
  assert.deepEqual(await unknownResponse.json(), { error: "invalid_token" });

  const revokedResponse = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-foreign/replay-link", {
      method: "POST",
      headers: { Authorization: "Bearer tok-revoked" },
    }),
    env as never,
  );

  assert.equal(revokedResponse.status, 401);
  assert.deepEqual(await revokedResponse.json(), { error: "invalid_token" });
});

test("replay-link returns 403 when battle belongs to another player", async () => {
  const env = buildEnv();
  await insertToken(
    env.DB,
    "tok-player-001",
    "player-account-001",
    "2026-01-01T00:00:00Z",
  );
  env.DB.v3Battles.set("battle-foreign", {
    battle_id: "battle-foreign",
    run_id: "run-foreign",
    installation_id: "inst_replay",
    player_account_id: "other-player",
    bundle_id: "bundle-foreign",
    recorded_at_utc: new Date().toISOString(),
    day: 7,
    player_name: "Remote",
    player_account_id_in_payload: "other-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "OtherLocal",
    opponent_account_id: "player-account-001",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-foreign/replay-link", {
      method: "POST",
      headers: { Authorization: "Bearer tok-player-001" },
    }),
    env as never,
  );

  assert.equal(response.status, 403);
  assert.deepEqual(await response.json(), { error: "replay_forbidden" });
});

test("replay-link returns download metadata when battle owner matches bearer", async () => {
  const env = buildEnv();
  await insertToken(
    env.DB,
    "tok-player-001",
    "player-account-001",
    "2026-01-01T00:00:00Z",
  );
  env.DB.v3Battles.set("battle-owned", {
    battle_id: "battle-owned",
    run_id: "run-owned",
    installation_id: "inst_replay",
    player_account_id: "player-account-001",
    bundle_id: "bundle-owned",
    recorded_at_utc: new Date().toISOString(),
    day: 8,
    player_name: "Local",
    player_account_id_in_payload: "different-payload-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1500,
    player_level: 10,
    opponent_name: "Remote",
    opponent_account_id: "other-player",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1510,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-owned/replay-link", {
      method: "POST",
      headers: { Authorization: "Bearer tok-player-001" },
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const payload = (await response.json()) as {
    download_url: string;
    expires_at_utc: string;
  };
  assert.deepEqual(Object.keys(payload).sort(), ["download_url", "expires_at_utc"]);
  assert.match(payload.download_url, /^https:\/\/example\.com\/replays\/replay_/);
  assert.match(payload.expires_at_utc, /^\d{4}-\d{2}-\d{2}T/);

  const replayToken = env.DB.v3ReplayTokens.get(payload.download_url.split("/").pop() ?? "");
  assert.ok(replayToken);
  assert.equal(replayToken.requested_by_player_account_id, "player-account-001");
  assert.equal(replayToken.expires_at_utc, payload.expires_at_utc);
});

test("replay-link allows unauthenticated creation when configured", async () => {
  const env = buildEnv();
  env.ALLOW_UNAUTHENTICATED_REPLAY_LINKS = "true";
  env.DB.v3Battles.set("battle-public-link", {
    battle_id: "battle-public-link",
    run_id: "run-public-link",
    installation_id: "inst_remote",
    player_account_id: "remote-player",
    bundle_id: "bundle-public-link",
    recorded_at_utc: new Date().toISOString(),
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

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-public-link/replay-link", {
      method: "POST",
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  const payload = (await response.json()) as {
    download_url: string;
    expires_at_utc: string;
  };
  assert.deepEqual(Object.keys(payload).sort(), ["download_url", "expires_at_utc"]);
  assert.match(payload.download_url, /^https:\/\/example\.com\/replays\/replay_/);
  assert.match(payload.expires_at_utc, /^\d{4}-\d{2}-\d{2}T/);

  const token = payload.download_url.split("/").pop();
  assert.ok(token);
  const replayToken = env.DB.v3ReplayTokens.get(token ?? "");
  assert.equal(replayToken?.requested_by_player_account_id, "remote-player");
  assert.equal(replayToken?.expires_at_utc, payload.expires_at_utc);
});

test("download replay accepts valid short-lived token", async () => {
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
  await env.RUN_BUNDLE_BUCKET.put(
    "run-bundles/remote-player/inst_replay/run-owned/payload-hash.mpack.gz",
    new TextEncoder().encode('{"battle_id":"battle-owned"}'),
    {
      httpMetadata: { contentType: "application/json" },
    },
  );

  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64("");
  const signature = signCanonical(
    privateKey,
    canonicalRequestV3({
      method: "GET",
      path: "/replays/token-valid",
      query: "",
      installationId: "inst_replay",
      timestamp,
      bodyHash,
    }),
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-valid", {
      method: "GET",
      headers: {
        "x-bpp-installation-id": "inst_replay",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature": signature,
      },
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.equal(await response.text(), '{"battle_id":"battle-owned"}');
});

test("download replay returns artifact_expired when artifact is no longer available", async () => {
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

  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64("");
  const signature = signCanonical(
    privateKey,
    canonicalRequestV3({
      method: "GET",
      path: "/replays/token-expired-artifact",
      query: "",
      installationId: "inst_replay",
      timestamp,
      bodyHash,
    }),
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-expired-artifact", {
      method: "GET",
      headers: {
        "x-bpp-installation-id": "inst_replay",
        "x-bpp-timestamp": timestamp,
        "x-bpp-content-sha256": bodyHash,
        "x-bpp-signature": signature,
      },
    }),
    env as never,
  );

  assert.equal(response.status, 410);
  assert.deepEqual(await response.json(), { error: "artifact_expired" });
});

test("download replay rejects a token created for another player", async () => {
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
  env.DB.v3ReplayTokens.set("token-foreign", {
    token: "token-foreign",
    battle_id: "battle-owned",
    requested_by_player_account_id: "other-player",
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

  const timestamp = new Date().toISOString();
  const bodyHash = sha256Base64("");
  const signature = signCanonical(
    privateKey,
    canonicalRequestV3({
      method: "GET",
      path: "/replays/token-foreign",
      query: "",
      installationId: "inst_replay",
      timestamp,
      bodyHash,
    }),
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-foreign", {
      method: "GET",
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
  assert.deepEqual(await response.json(), { error: "replay_token_forbidden" });
});

test("download replay allows bearer-token access when unauthenticated downloads are enabled", async () => {
  const env = buildEnv();
  env.ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS = "true";
  env.DB.v3ReplayTokens.set("token-public", {
    token: "token-public",
    battle_id: "battle-public",
    requested_by_player_account_id: "other-player",
    expires_at_utc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    created_at_utc: new Date().toISOString(),
    used_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.v3Battles.set("battle-public", {
    battle_id: "battle-public",
    run_id: "run-public",
    installation_id: "inst_remote",
    player_account_id: "remote-player",
    bundle_id: "bundle-public",
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
  env.DB.v3RunBundles.set("bundle-public", {
    bundle_id: "bundle-public",
    installation_id: "inst_remote",
    player_account_id: "remote-player",
    run_id: "run-public",
    payload_hash: "payload-hash-public",
    schema_version: 3,
    object_key: "run-bundles/remote-player/inst_remote/run-public/payload-hash-public.mpack.gz",
    codec: "application/json",
    size_bytes: 12,
    submitted_at_utc: new Date().toISOString(),
    created_at_utc: new Date().toISOString(),
  });
  await env.RUN_BUNDLE_BUCKET.put(
    "run-bundles/remote-player/inst_remote/run-public/payload-hash-public.mpack.gz",
    new TextEncoder().encode('{"battle_id":"battle-public"}'),
    {
      httpMetadata: { contentType: "application/json" },
    },
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-public", {
      method: "GET",
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.equal(await response.text(), '{"battle_id":"battle-public"}');
});

test("download replay accepts unsigned installation requests", async () => {
  const env = buildEnv();
  env.DB.v3Installations.set("inst_unsigned", {
    installation_id: "inst_unsigned",
    player_account_id: "player-account-unsigned",
    public_key: JSON.stringify({
      modulus_b64: "unused",
      exponent_b64: "unused",
    }),
    status: "active",
    created_at_utc: new Date().toISOString(),
    last_seen_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.v3ReplayTokens.set("token-unsigned", {
    token: "token-unsigned",
    battle_id: "battle-owned-unsigned",
    requested_by_player_account_id: "player-account-unsigned",
    expires_at_utc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    created_at_utc: new Date().toISOString(),
    used_at_utc: null,
    revoked_at_utc: null,
  });
  env.DB.v3Battles.set("battle-owned-unsigned", {
    battle_id: "battle-owned-unsigned",
    run_id: "run-owned-unsigned",
    installation_id: "inst_remote",
    player_account_id: "remote-player",
    bundle_id: "bundle-owned-unsigned",
    recorded_at_utc: new Date().toISOString(),
    day: 9,
    player_name: "Remote",
    player_account_id_in_payload: "remote-player",
    player_hero: "HeroA",
    player_rank: "Gold",
    player_rating: 1600,
    player_level: 10,
    opponent_name: "Local",
    opponent_account_id: "player-account-unsigned",
    opponent_hero: "HeroB",
    opponent_rank: "Gold",
    opponent_rating: 1610,
    opponent_level: 11,
    result: "Won",
    replay_available: 1,
    updated_at_utc: new Date().toISOString(),
  });
  env.DB.v3RunBundles.set("bundle-owned-unsigned", {
    bundle_id: "bundle-owned-unsigned",
    installation_id: "inst_remote",
    player_account_id: "remote-player",
    run_id: "run-owned-unsigned",
    payload_hash: "payload-hash-unsigned",
    schema_version: 3,
    object_key: "run-bundles/remote-player/inst_remote/run-owned-unsigned/payload-hash-unsigned.mpack.gz",
    codec: "application/json",
    size_bytes: 12,
    submitted_at_utc: new Date().toISOString(),
    created_at_utc: new Date().toISOString(),
  });
  await env.RUN_BUNDLE_BUCKET.put(
    "run-bundles/remote-player/inst_remote/run-owned-unsigned/payload-hash-unsigned.mpack.gz",
    new TextEncoder().encode('{"battle_id":"battle-owned-unsigned"}'),
    {
      httpMetadata: { contentType: "application/json" },
    },
  );

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-unsigned", {
      method: "GET",
      headers: {
        "x-bpp-installation-id": "inst_unsigned",
      },
    }),
    env as never,
  );

  assert.equal(response.status, 200);
  assert.equal(await response.text(), '{"battle_id":"battle-owned-unsigned"}');
});
