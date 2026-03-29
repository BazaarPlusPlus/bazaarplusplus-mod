import type { Env } from "../env";
import { json } from "../http/json";
import { trimString } from "../http/request";
import {
  batchExecute,
  replaceProjectedBattlesForRun,
} from "../persistence/battleProjections";
import {
  buildPlayerAccountUpsert,
  getActiveBindingUid,
  listObservedPlayerAccountIds,
} from "../persistence/bindings";
import {
  markRunProjectionStatus,
  upsertRunUpload,
} from "../persistence/runUploads";
import {
  parseRunUploadBody,
  type ParsedRunBattle,
} from "./uploadRunPayload";
import { requireVerifiedClient } from "./verifiedClient";

export async function handleRunUpload(
  request: Request,
  env: Env,
): Promise<Response> {
  const verified = await requireVerifiedClient(request, env, "runs", {
    consumeNonce: true,
  });
  if (verified instanceof Response) {
    return verified;
  }

  const parsed = parseRunUploadBody(verified.payload);
  if (parsed instanceof Response) {
    return parsed;
  }

  const headerRunId = trimString(request.headers.get("x-bpp-run-id")) || null;
  const runId = headerRunId ?? parsed.runId;
  if (!runId) {
    return json({ error: "run_id_required" }, { status: 400 });
  }

  if (headerRunId && parsed.runId && headerRunId !== parsed.runId) {
    return json({ error: "run_id_mismatch" }, { status: 400 });
  }

  const uploadedAtUtc = new Date().toISOString();
  await upsertRunUpload(env, {
    clientId: verified.client.client_id,
    installId: verified.client.install_id,
    runId,
    payloadSha256: verified.payloadHash,
    uploadedAtUtc,
    projectionStatus: "pending",
    projectedAtUtc: null,
    projectionError: null,
  });

  try {
    const observedAtUtc = new Date().toISOString();
    const boundUid = await getActiveBindingUid(env, verified.client.client_id);
    const projectedBattles = parsed.battles
      .filter(
        (battle) =>
          battle.combatKind === "PVPCombat" && !!battle.battleId,
      )
      .map((battle) => ({
        battleId: battle.battleId!,
        runId,
        sourceClientId: verified.client.client_id,
        recordedAtUtc: battle.recordedAtUtc ?? observedAtUtc,
        day: battle.day,
        hour: battle.hour,
        encounterId: battle.encounterId,
        playerName: battle.playerName,
        playerAccountId: battle.playerAccountId,
        playerHero: battle.playerHero,
        playerRank: battle.playerRank,
        playerRating: battle.playerRating,
        playerLevel: battle.playerLevel,
        opponentName: battle.opponentName,
        opponentAccountId: battle.opponentAccountId,
        opponentHero: battle.opponentHero,
        opponentRank: battle.opponentRank,
        opponentRating: battle.opponentRating,
        opponentLevel: battle.opponentLevel,
        combatKind: battle.combatKind!,
        result: battle.result,
        winnerCombatantId: battle.winnerCombatantId,
        loserCombatantId: battle.loserCombatantId,
        payloadJson: JSON.stringify(battle.raw),
        createdAtUtc: observedAtUtc,
        updatedAtUtc: observedAtUtc,
      }));

    await replaceProjectedBattlesForRun(env, {
      sourceClientId: verified.client.client_id,
      runId,
      battles: projectedBattles,
    });

    if (boundUid) {
      const observedPlayerAccountIds = distinctNonEmptyPlayerAccountIds(parsed.battles);
      if (observedPlayerAccountIds.length === 1) {
        const existingPlayerAccountIds = await listObservedPlayerAccountIds(env, boundUid);
        const playerAccountId = observedPlayerAccountIds[0]!;
        if (
          existingPlayerAccountIds.length === 0
          || existingPlayerAccountIds.includes(playerAccountId)
        ) {
          await batchExecute(env, [
            buildPlayerAccountUpsert(env, {
              uid: boundUid,
              playerAccountId,
              lastClientId: verified.client.client_id,
              observedAtUtc,
            }),
          ]);
        }
      }
    }

    await markRunProjectionStatus(env, {
      runId,
      projectionStatus: "projected",
      projectedAtUtc: observedAtUtc,
      projectionError: null,
    });
  } catch (error) {
    const message =
      error instanceof Error ? error.message : "Unknown projection error";
    await markRunProjectionStatus(env, {
      runId,
      projectionStatus: "failed",
      projectedAtUtc: null,
      projectionError: message,
    });
    return json({ error: "projection_failed" }, { status: 500 });
  }

  return json({ status: "accepted" });
}

function distinctNonEmptyPlayerAccountIds(battles: ParsedRunBattle[]): string[] {
  const values = new Set<string>();
  for (const battle of battles) {
    if (battle.playerAccountId) {
      values.add(battle.playerAccountId);
    }
  }

  return Array.from(values);
}
