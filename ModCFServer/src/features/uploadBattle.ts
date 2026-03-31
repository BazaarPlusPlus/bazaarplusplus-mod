import type { Env } from "../env";
import { sha256Base64 } from "../crypto/hash";
import { json } from "../http/json";
import { trimString } from "../http/request";
import { logResponseWarning } from "../observability";
import { upsertProjectedBattle } from "../persistence/battleProjections";
import { requireVerifiedClient } from "./verifiedClient";
import { parseBattleUploadBody } from "./uploadBattlePayload";

export async function handleBattleUpload(
  request: Request,
  env: Env,
): Promise<Response> {
  const battleId = trimString(request.headers.get("x-bpp-battle-id"));
  if (!battleId) {
    return json({ error: "battle_id_required" }, { status: 400 });
  }

  const verified = await requireVerifiedClient(request, env, "replays");
  if (verified instanceof Response) {
    return verified;
  }

  const parsed = parseBattleUploadBody(verified.payload, battleId);
  if (parsed instanceof Response) {
    await logResponseWarning("battle_upload.rejected", parsed, {
      route: "/battles/upload",
      client_id: verified.client.client_id,
      install_id: verified.client.install_id,
      battle_id: battleId,
      run_id: trimString(request.headers.get("x-bpp-run-id")) || null,
    });
    return parsed;
  }

  const headerRunId = trimString(request.headers.get("x-bpp-run-id")) || null;
  if (headerRunId && parsed.runId && headerRunId !== parsed.runId) {
    const response = json({ error: "run_id_mismatch" }, { status: 400 });
    await logResponseWarning("battle_upload.rejected", response, {
      route: "/battles/upload",
      client_id: verified.client.client_id,
      install_id: verified.client.install_id,
      battle_id: battleId,
      run_id: parsed.runId,
    });
    return response;
  }

  const uploadedAtUtc = new Date().toISOString();
  const contentType = request.headers.get("content-type") ?? "application/json";
  const battlePayloadBytes = new TextEncoder().encode(parsed.battlePayloadJson);
  const battlePayloadBuffer = battlePayloadBytes.buffer.slice(
    battlePayloadBytes.byteOffset,
    battlePayloadBytes.byteOffset + battlePayloadBytes.byteLength,
  ) as ArrayBuffer;
  const replayPayloadHash = await sha256Base64(battlePayloadBuffer);
  const objectKey = `replays/${verified.client.client_id}/${battleId}/${replayPayloadHash}.json`;
  await env.PVP_BATTLE_BUCKET.put(
    objectKey,
    battlePayloadBytes,
    {
      httpMetadata: {
        contentType,
      },
    },
  );

  await upsertProjectedBattle(env, {
    battleId,
    runId: parsed.runId,
    sourceClientId: verified.client.client_id,
    recordedAtUtc: parsed.manifest.recordedAtUtc,
    day: parsed.manifest.day,
    hour: parsed.manifest.hour,
    encounterId: parsed.manifest.encounterId,
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
    replayAvailable: 1,
    replayObjectKey: objectKey,
    replayUploadedAtUtc: uploadedAtUtc,
    createdAtUtc: uploadedAtUtc,
    updatedAtUtc: uploadedAtUtc,
  });

  return json({
    status: "accepted",
    object_key: objectKey,
  });
}
