import { json } from "../http/json";
import { trimString } from "../http/request";

const textDecoder = new TextDecoder();

type JsonObject = Record<string, unknown>;

export type ParsedBattleUploadBody = {
  battleId: string;
  runId: string | null;
  schemaVersion: number | null;
  manifest: {
    battleId: string;
    runId: string | null;
    recordedAtUtc: string;
    day: number | null;
    hour: number | null;
    encounterId: string | null;
    playerName: string | null;
    playerAccountId: string | null;
    playerHero: string | null;
    playerRank: string | null;
    playerRating: number | null;
    playerLevel: number | null;
    opponentName: string | null;
    opponentAccountId: string | null;
    opponentHero: string | null;
    opponentRank: string | null;
    opponentRating: number | null;
    opponentLevel: number | null;
    combatKind: string;
    result: string | null;
    winnerCombatantId: string | null;
    loserCombatantId: string | null;
  };
  replaySchemaVersion: number;
  battlePayloadJson: string;
};

function asObject(value: unknown): JsonObject | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return null;
  }

  return value as JsonObject;
}

function asString(value: unknown): string | null {
  return typeof value === "string" ? trimString(value) || null : null;
}

function asNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

export function parseBattleUploadBody(
  payload: ArrayBuffer,
  headerBattleId: string,
): ParsedBattleUploadBody | Response {
  try {
    const parsed = JSON.parse(textDecoder.decode(payload));
    const raw = asObject(parsed);
    if (raw == null) {
      return json({ error: "invalid_json_body" }, { status: 400 });
    }

    const battleId = asString(raw.battle_id);
    if (!battleId) {
      return json({ error: "battle_id_required" }, { status: 400 });
    }

    if (battleId !== headerBattleId) {
      return json({ error: "battle_id_mismatch" }, { status: 400 });
    }

    const battleManifest = asObject(raw.battle_manifest);
    if (battleManifest == null) {
      return json({ error: "battle_manifest_required" }, { status: 400 });
    }

    const manifestBattleId = asString(battleManifest.battle_id);
    const recordedAtUtc = asString(battleManifest.recorded_at_utc);
    const combatKind = asString(battleManifest.combat_kind);
    const participants = asObject(battleManifest.participants);
    const outcome = asObject(battleManifest.outcome) ?? {};
    const snapshots = asObject(battleManifest.snapshots);
    if (
      manifestBattleId !== battleId
      || !recordedAtUtc
      || !combatKind
      || participants == null
      || snapshots == null
    ) {
      return json({ error: "invalid_battle_manifest" }, { status: 400 });
    }

    const opponentAccountId = asString(participants.opponent_account_id);
    if (combatKind === "PVPCombat" && !opponentAccountId) {
      return json({ error: "invalid_battle_manifest" }, { status: 400 });
    }

    const replayPayload = asObject(raw.replay_payload);
    if (replayPayload == null) {
      return json({ error: "replay_payload_required" }, { status: 400 });
    }

    const replayBattleId = asString(replayPayload.battle_id);
    const replaySchemaVersion = asNumber(replayPayload.version);
    const spawnMessageBase64 = asString(replayPayload.spawn_message_base64);
    const combatMessageBase64 = asString(replayPayload.combat_message_base64);
    const despawnMessageBase64 = asString(replayPayload.despawn_message_base64);
    if (
      replayBattleId !== battleId
      || replaySchemaVersion == null
      || !spawnMessageBase64
      || !combatMessageBase64
      || !despawnMessageBase64
    ) {
      return json({ error: "invalid_replay_payload" }, { status: 400 });
    }

    const topLevelRunId = asString(raw.run_id);
    const manifestRunId = asString(battleManifest.run_id);
    if (topLevelRunId && manifestRunId && topLevelRunId !== manifestRunId) {
      return json({ error: "run_id_mismatch" }, { status: 400 });
    }

    return {
      battleId,
      runId: topLevelRunId ?? manifestRunId,
      schemaVersion: asNumber(raw.schema_version),
      manifest: {
        battleId,
        runId: manifestRunId,
        recordedAtUtc,
        day: asNumber(battleManifest.day),
        hour: asNumber(battleManifest.hour),
        encounterId: asString(battleManifest.encounter_id),
        playerName: asString(participants.player_name),
        playerAccountId: asString(participants.player_account_id),
        playerHero: asString(participants.player_hero),
        playerRank: asString(participants.player_rank),
        playerRating: asNumber(participants.player_rating),
        playerLevel: asNumber(participants.player_level),
        opponentName: asString(participants.opponent_name),
        opponentAccountId,
        opponentHero: asString(participants.opponent_hero),
        opponentRank: asString(participants.opponent_rank),
        opponentRating: asNumber(participants.opponent_rating),
        opponentLevel: asNumber(participants.opponent_level),
        combatKind,
        result: asString(outcome.result),
        winnerCombatantId: asString(outcome.winner_combatant_id),
        loserCombatantId: asString(outcome.loser_combatant_id),
      },
      replaySchemaVersion,
      battlePayloadJson: JSON.stringify({
        battle_id: battleId,
        battle_manifest: battleManifest,
        replay_payload: replayPayload,
      }),
    };
  } catch {
    return json({ error: "invalid_json_body" }, { status: 400 });
  }
}
