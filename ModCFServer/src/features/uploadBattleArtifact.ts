import { sha256Base64 } from "../crypto/hash";
import type { Env } from "../env";
import { json } from "../http/json";
import { logInfo } from "../observability";
import { getPlayerLink } from "../persistence/playerLinks";
import { upsertBattle } from "../persistence/battles";
import { gzipBytes } from "./replayCompression";
import { parseBattleUploadBody } from "./uploadBattlePayload";
import { requireVerifiedClient } from "./verifiedClient";

function shouldDiscardBattleUpload(input: {
  day: number | null;
  playerRating: number | null;
}): boolean {
  if (input.playerRating != null && input.playerRating > 820) {
    return false;
  }

  if (input.day != null && input.day > 11) {
    return false;
  }

  return true;
}

function tryReadBattleIdFromPayload(payload: ArrayBuffer): string | null {
  try {
    const parsed = JSON.parse(new TextDecoder().decode(payload)) as {
      battle_id?: unknown;
    };
    return typeof parsed.battle_id === "string" && parsed.battle_id.trim()
      ? parsed.battle_id.trim()
      : null;
  } catch {
    return null;
  }
}

export async function handleUploadBattleArtifact(
  request: Request,
  env: Env,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env);
  if (verified instanceof Response) {
    return verified;
  }

  const headerBattleId =
    request.headers.get("x-bpp-battle-id") ?? tryReadBattleIdFromPayload(verified.payload);
  const parsed = parseBattleUploadBody(verified.payload, headerBattleId ?? "");
  if (parsed instanceof Response) {
    return parsed;
  }

  if (
    shouldDiscardBattleUpload({
      day: parsed.manifest.day,
      playerRating: parsed.manifest.playerRating,
    })
  ) {
    logInfo("battle.discarded", {
      battle_id: parsed.battleId,
      run_id: parsed.runId,
      client_id: verified.client.client_id,
      day: parsed.manifest.day,
      player_rating: parsed.manifest.playerRating,
      reason: "low_mmr_before_day_10",
    });
    return json({
      status: "accepted",
      discarded: true,
      reason: "low_mmr_before_day_10",
    });
  }

  const replayPayloadBytes = new TextEncoder().encode(parsed.battlePayloadJson);
  const replayPayloadHash = await sha256Base64(replayPayloadBytes);
  const compressedReplayPayloadBytes = await gzipBytes(replayPayloadBytes);
  const objectKey = `battle-replays/${verified.client.client_id}/${parsed.battleId}/${replayPayloadHash}.json`;
  await env.PVP_BATTLE_BUCKET.put(objectKey, compressedReplayPayloadBytes, {
    httpMetadata: {
      contentType: "application/json; charset=utf-8",
      contentEncoding: "gzip",
    },
    customMetadata: {
      "bpp-storage-codec": "gzip",
      "bpp-compressed-size-bytes": String(compressedReplayPayloadBytes.byteLength),
    },
  });

  const uploadedAtUtc = new Date().toISOString();
  const playerLink = await getPlayerLink(env, verified.client.client_id);
  await upsertBattle(env, {
    battleId: parsed.battleId,
    runId: parsed.runId,
    clientId: verified.client.client_id,
    uploaderPlayerAccountId: playerLink?.player_account_id ?? null,
    recordedAtUtc: parsed.manifest.recordedAtUtc,
    day: parsed.manifest.day,
    hour: parsed.manifest.hour,
    playerName: parsed.manifest.playerName,
    playerAccountId: parsed.manifest.playerAccountId,
    playerHero: parsed.manifest.playerHero,
    playerRank: parsed.manifest.playerRank,
    playerRating: parsed.manifest.playerRating,
    playerLevel: parsed.manifest.playerLevel,
    opponentName: parsed.manifest.opponentName,
    opponentAccountId: parsed.manifest.opponentAccountId,
    opponentHero: parsed.manifest.opponentHero,
    opponentRank: parsed.manifest.opponentRank,
    opponentRating: parsed.manifest.opponentRating,
    opponentLevel: parsed.manifest.opponentLevel,
    combatKind: parsed.manifest.combatKind,
    result: parsed.manifest.result,
    winnerCombatantId: parsed.manifest.winnerCombatantId,
    loserCombatantId: parsed.manifest.loserCombatantId,
    replaySchemaVersion: parsed.replaySchemaVersion,
    replayObjectKey: objectKey,
    replaySizeBytes: replayPayloadBytes.byteLength,
    createdAtUtc: uploadedAtUtc,
    updatedAtUtc: uploadedAtUtc,
  });

  logInfo("battle.accepted", {
    battle_id: parsed.battleId,
    run_id: parsed.runId,
    client_id: verified.client.client_id,
    uploader_player_account_id: playerLink?.player_account_id ?? null,
    day: parsed.manifest.day,
    player_rating: parsed.manifest.playerRating,
    replay_object_key: objectKey,
  });

  return json({
    status: "accepted",
    object_key: objectKey,
  });
}
