import type { Env } from "../env";
import { json } from "../http/json";
import {
  getProjectedBattleById,
  listProjectedBattlesAgainstAccountIds,
} from "../persistence/battleProjections";
import {
  getActiveBoundPlayerAccountId,
} from "../persistence/bindings";
import { getReplayUploadByBattleId } from "../persistence/replayUploads";
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

function withReplayAvailability(
  summaryJson: string,
  replayAvailable: boolean,
): AgainstMeBattlePayload {
  const parsed = JSON.parse(summaryJson) as AgainstMeBattlePayload;
  return {
    ...parsed,
    replay: {
      available: replayAvailable,
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
    battles: battles.map((battle) =>
      withReplayAvailability(
        battle.summary_json ?? battle.payload_json,
        battle.replay_available !== 0,
      ),
    ),
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

  const replayUpload = await getReplayUploadByBattleId(env, battleId);
  if (!replayUpload) {
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
    replay_uploaded_at_utc: replayUpload.uploaded_at_utc,
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

  const replayUpload = await getReplayUploadByBattleId(env, battleId);
  if (!replayUpload) {
    return json({ error: "replay_not_found" }, { status: 404 });
  }

  const object = await env.REPLAY_BUCKET.get(replayUpload.object_key);
  if (!object) {
    return json({ error: "replay_not_found" }, { status: 404 });
  }

  return new Response(await object.arrayBuffer(), {
    headers: {
      "content-type": "application/json; charset=utf-8",
    },
  });
}
