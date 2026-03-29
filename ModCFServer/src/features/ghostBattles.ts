import type { Env } from "../env";
import { json } from "../http/json";
import {
  getProjectedBattleById,
  listProjectedBattlesAgainstAccountIds,
} from "../persistence/battleProjections";
import {
  getActiveBoundPlayerAccountId,
} from "../persistence/bindings";
import { requireVerifiedClient } from "./verifiedClient";

const DEFAULT_LOOKBACK_DAYS = 3;
const MAX_LOOKBACK_DAYS = 14;
const DEFAULT_LIMIT = 50;
const MAX_LIMIT = 200;
const MAX_ACCOUNT_IDS = 50;
const DOWNLOAD_TTL_SECONDS = 5 * 60;

type AgainstMeBattlePayload = {
  battle_id?: unknown;
  [key: string]: unknown;
};

function parsePositiveInteger(
  value: string | null,
  fallbackValue: number,
  maxValue: number,
): number {
  const parsed = Number.parseInt(value ?? "", 10);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    return fallbackValue;
  }

  return Math.min(parsed, maxValue);
}

function subtractDaysIso(now: Date, days: number): string {
  return new Date(now.getTime() - days * 24 * 60 * 60 * 1000).toISOString();
}

async function createDownloadSignature(
  secret: string,
  battleId: string,
  expiresAtUnixSeconds: number,
): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const signature = await crypto.subtle.sign(
    "HMAC",
    key,
    new TextEncoder().encode(
      ["GET", "/replays/download", battleId, String(expiresAtUnixSeconds)].join("\n"),
    ),
  );

  const bytes = new Uint8Array(signature);
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary)
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
}

async function isValidDownloadSignature(
  secret: string,
  battleId: string,
  expiresAtUnixSeconds: number,
  signature: string,
): Promise<boolean> {
  const expected = await createDownloadSignature(
    secret,
    battleId,
    expiresAtUnixSeconds,
  );
  if (expected.length !== signature.length) {
    return false;
  }

  const a = new TextEncoder().encode(expected);
  const b = new TextEncoder().encode(signature);
  let diff = 0;
  for (let i = 0; i < a.length; i += 1) {
    diff |= a[i]! ^ b[i]!;
  }

  return diff === 0;
}

function buildBattlePayload(
  battle: {
    battle_id: string;
    run_id?: string | null;
    recorded_at_utc: string;
    day?: number | null;
    hour?: number | null;
    encounter_id?: string | null;
    player_name?: string | null;
    player_account_id?: string | null;
    player_hero?: string | null;
    player_rank?: string | null;
    player_rating?: number | null;
    player_level?: number | null;
    opponent_name?: string | null;
    opponent_account_id: string | null;
    opponent_hero?: string | null;
    opponent_rank?: string | null;
    opponent_rating?: number | null;
    opponent_level?: number | null;
    combat_kind?: string;
    result?: string | null;
    winner_combatant_id?: string | null;
    loser_combatant_id?: string | null;
    replay_available: number;
  },
): AgainstMeBattlePayload {
  return {
    battle_id: battle.battle_id,
    run_id: battle.run_id ?? null,
    recorded_at_utc: battle.recorded_at_utc,
    day: battle.day ?? null,
    hour: battle.hour ?? null,
    encounter_id: battle.encounter_id ?? null,
    player_name: battle.player_name ?? null,
    player_account_id: battle.player_account_id ?? null,
    player_hero: battle.player_hero ?? null,
    player_rank: battle.player_rank ?? null,
    player_rating: battle.player_rating ?? null,
    player_level: battle.player_level ?? null,
    opponent_name: battle.opponent_name ?? null,
    opponent_account_id: battle.opponent_account_id ?? null,
    opponent_hero: battle.opponent_hero ?? null,
    opponent_rank: battle.opponent_rank ?? null,
    opponent_rating: battle.opponent_rating ?? null,
    opponent_level: battle.opponent_level ?? null,
    combat_kind: battle.combat_kind ?? "PVPCombat",
    result: battle.result ?? null,
    winner_combatant_id: battle.winner_combatant_id ?? null,
    loser_combatant_id: battle.loser_combatant_id ?? null,
    replay: {
      available: battle.replay_available !== 0,
    },
  };
}

export async function handleGhostBattlesAgainstMe(
  request: Request,
  env: Env,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env, "runs");
  if (verified instanceof Response) {
    return verified;
  }

  const boundPlayerAccountId = await getActiveBoundPlayerAccountId(
    env,
    verified.client.client_id,
  );
  if (!boundPlayerAccountId) {
    return json({
      resolved_account_ids: [],
      from_utc: subtractDaysIso(new Date(), DEFAULT_LOOKBACK_DAYS),
      to_utc: new Date().toISOString(),
      battles: [],
    });
  }

  const url = new URL(request.url);
  const lookbackDays = parsePositiveInteger(
    url.searchParams.get("days"),
    DEFAULT_LOOKBACK_DAYS,
    MAX_LOOKBACK_DAYS,
  );
  const limit = parsePositiveInteger(
    url.searchParams.get("limit"),
    DEFAULT_LIMIT,
    MAX_LIMIT,
  );
  const now = new Date();
  const fromUtc = subtractDaysIso(now, lookbackDays);
  const resolvedAccountIds = [boundPlayerAccountId].slice(0, MAX_ACCOUNT_IDS);
  const battles = await listProjectedBattlesAgainstAccountIds(env, {
    opponentAccountIds: resolvedAccountIds,
    fromUtc,
    limit,
  });

  return json({
    resolved_account_ids: resolvedAccountIds,
    from_utc: fromUtc,
    to_utc: now.toISOString(),
    battles: battles.map((battle) => buildBattlePayload(battle)),
  });
}

export async function handleGhostBattleReplayDownloadLink(
  request: Request,
  env: Env,
  battleId: string,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env, "runs");
  if (verified instanceof Response) {
    return verified;
  }

  const boundPlayerAccountId = await getActiveBoundPlayerAccountId(
    env,
    verified.client.client_id,
  );
  if (!boundPlayerAccountId) {
    return json({ error: "battle_forbidden" }, { status: 403 });
  }

  const battle = await getProjectedBattleById(env, battleId);
  if (!battle) {
    return json({ error: "battle_not_found" }, { status: 404 });
  }

  if (battle.opponent_account_id !== boundPlayerAccountId) {
    return json({ error: "battle_forbidden" }, { status: 403 });
  }

  if (!battle.replay_object_key || !battle.replay_uploaded_at_utc) {
    return json({ error: "replay_unavailable" }, { status: 409 });
  }

  const expiresAtUnixSeconds = Math.floor(Date.now() / 1000) + DOWNLOAD_TTL_SECONDS;
  const signature = await createDownloadSignature(
    env.REPLAY_DOWNLOAD_SECRET,
    battleId,
    expiresAtUnixSeconds,
  );
  const url = new URL(request.url);
  const downloadUrl = new URL("/replays/download", url.origin);
  downloadUrl.searchParams.set("battle_id", battleId);
  downloadUrl.searchParams.set("exp", String(expiresAtUnixSeconds));
  downloadUrl.searchParams.set("sig", signature);

  return json({
    battle_id: battleId,
    expires_at_utc: new Date(expiresAtUnixSeconds * 1000).toISOString(),
    download_url: downloadUrl.toString(),
    replay_uploaded_at_utc: battle.replay_uploaded_at_utc,
  });
}

export async function handleReplayDownload(
  request: Request,
  env: Env,
): Promise<Response> {
  const url = new URL(request.url);
  const battleId = url.searchParams.get("battle_id")?.trim() ?? "";
  const expValue = url.searchParams.get("exp")?.trim() ?? "";
  const signature = url.searchParams.get("sig")?.trim() ?? "";
  if (!battleId || !expValue || !signature) {
    return json({ error: "download_token_required" }, { status: 400 });
  }

  const expiresAtUnixSeconds = Number.parseInt(expValue, 10);
  if (!Number.isFinite(expiresAtUnixSeconds) || expiresAtUnixSeconds <= 0) {
    return json({ error: "invalid_download_token" }, { status: 400 });
  }

  if (expiresAtUnixSeconds < Math.floor(Date.now() / 1000)) {
    return json({ error: "download_token_expired" }, { status: 410 });
  }

  const signatureValid = await isValidDownloadSignature(
    env.REPLAY_DOWNLOAD_SECRET,
    battleId,
    expiresAtUnixSeconds,
    signature,
  );
  if (!signatureValid) {
    return json({ error: "invalid_download_signature" }, { status: 403 });
  }

  const battle = await getProjectedBattleById(env, battleId);
  if (!battle) {
    return json({ error: "battle_not_found" }, { status: 404 });
  }

  if (!battle.replay_object_key) {
    return json({ error: "replay_not_found" }, { status: 404 });
  }

  const object = await env.PVP_BATTLE_BUCKET.get(battle.replay_object_key);
  if (!object) {
    return json({ error: "replay_not_found" }, { status: 404 });
  }

  const payloadText = new TextDecoder().decode(await object.arrayBuffer());
  try {
    JSON.parse(payloadText);
  } catch {
    return json({ error: "replay_corrupt" }, { status: 500 });
  }

  return new Response(payloadText, {
    status: 200,
    headers: {
      "content-type": object.httpMetadata?.contentType ?? "application/json; charset=utf-8",
    },
  });
}
