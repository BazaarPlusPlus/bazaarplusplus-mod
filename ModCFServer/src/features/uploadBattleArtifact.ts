import { sha256Base64 } from "../crypto/hash";
import type { Env } from "../env";
import { json } from "../http/json";
import { getPlayerLink } from "../persistence/playerLinks";
import { upsertBattle } from "../persistence/battles";
import { parseBattleUploadBody } from "./uploadBattlePayload";
import { requireVerifiedClient } from "./verifiedClient";

export async function handleUploadBattleArtifact(
  request: Request,
  env: Env,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env);
  if (verified instanceof Response) {
    return verified;
  }

  const parsed = parseBattleUploadBody(verified.payload, request.headers.get("x-bpp-battle-id") ?? JSON.parse(new TextDecoder().decode(verified.payload)).battle_id);
  if (parsed instanceof Response) {
    return parsed;
  }

  const replayPayloadBytes = new TextEncoder().encode(parsed.battlePayloadJson);
  const replayPayloadBuffer = replayPayloadBytes.buffer.slice(
    replayPayloadBytes.byteOffset,
    replayPayloadBytes.byteOffset + replayPayloadBytes.byteLength,
  ) as ArrayBuffer;
  const replayPayloadHash = await sha256Base64(replayPayloadBuffer);
  const objectKey = `battle-replays/${verified.client.client_id}/${parsed.battleId}/${replayPayloadHash}.json`;
  await env.PVP_BATTLE_BUCKET.put(objectKey, replayPayloadBytes, {
    httpMetadata: {
      contentType: "application/json; charset=utf-8",
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

  return json({
    status: "accepted",
    object_key: objectKey,
  });
}
